using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Stocky.Api.Data;
using Stocky.Api.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Sql")
    ?? builder.Configuration["Sql:ConnectionString"];

builder.Services.AddDbContext<StockyDbContext>(options =>
{
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
    }
    else
    {
        options.UseInMemoryDatabase("stocky-dev");
    }
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme);

var entraClientIdAtStartup = builder.Configuration["AzureAd:ClientId"];
if (!string.IsNullOrWhiteSpace(entraClientIdAtStartup))
{
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
}

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddMemoryCache();

// Market data: prefer Alpaca when API credentials are configured, otherwise
// fall back to the deterministic stub so dev still works without secrets.
// Configure with:
//   MarketData:Alpaca:ApiKeyId      (APCA-API-KEY-ID)
//   MarketData:Alpaca:ApiSecret     (APCA-API-SECRET-KEY)
//   MarketData:Alpaca:BaseUrl       (optional, default https://data.alpaca.markets/)
var alpacaKeyId = builder.Configuration["MarketData:Alpaca:ApiKeyId"];
var alpacaSecret = builder.Configuration["MarketData:Alpaca:ApiSecret"];
builder.Services.AddSingleton<StubMarketDataProvider>();
if (!string.IsNullOrWhiteSpace(alpacaKeyId) && !string.IsNullOrWhiteSpace(alpacaSecret))
{
    var alpacaBase = builder.Configuration["MarketData:Alpaca:BaseUrl"] ?? "https://data.alpaca.markets/";
    builder.Services.AddHttpClient<AlpacaMarketDataProvider>(client =>
    {
        client.BaseAddress = new Uri(alpacaBase);
        client.DefaultRequestHeaders.Add("APCA-API-KEY-ID", alpacaKeyId);
        client.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", alpacaSecret);
        client.Timeout = TimeSpan.FromSeconds(10);
    });
    builder.Services.AddScoped<IMarketDataProvider>(sp => sp.GetRequiredService<AlpacaMarketDataProvider>());
}
else
{
    builder.Services.AddScoped<IMarketDataProvider>(sp => sp.GetRequiredService<StubMarketDataProvider>());
}
builder.Services.AddScoped<AlertEvaluator>();
builder.Services.AddScoped<TaxLotService>();
builder.Services.AddScoped<PortfolioLedgerService>();
builder.Services.AddScoped<PortfolioHistoryService>();
builder.Services.AddHostedService<QuoteRefresher>();
builder.Services.AddHostedService<SnapshotJob>();

var appInsightsConnection = builder.Configuration["ApplicationInsights:ConnectionString"]
    ?? builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrWhiteSpace(appInsightsConnection))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor(o => o.ConnectionString = appInsightsConnection);
}

const string CorsPolicy = "StockyCors";
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
    {
        var origins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        if (origins.Length == 0)
        {
            origins = new[] { "http://localhost:5173" };
        }
        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHttpsRedirection();
}

app.UseCors(CorsPolicy);

// Dev-only auth bypass: when Entra isn't configured, inject a synthetic user so the UI is browsable.
var entraClientId = builder.Configuration["AzureAd:ClientId"];
if (app.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(entraClientId))
{
    app.Use(async (ctx, next) =>
    {
        var identity = new System.Security.Claims.ClaimsIdentity(new[]
        {
            new System.Security.Claims.Claim("oid", "00000000-0000-0000-0000-000000000001"),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "Local Dev User")
        }, "DevBypass");
        ctx.User = new System.Security.Claims.ClaimsPrincipal(identity);
        await next();
    });
}
else
{
    app.UseAuthentication();
}
app.UseAuthorization();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<StockyDbContext>();
    if (db.Database.IsRelational())
    {
        await db.Database.MigrateAsync();
    }
}

app.Run();
