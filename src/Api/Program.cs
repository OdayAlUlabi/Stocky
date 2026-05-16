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

// Distributed cache: prefer Redis when Cache:RedisConnectionString is set,
// otherwise fall back to the in-memory distributed cache so single-instance
// dev still benefits from the same code path.
var redisConn = builder.Configuration["Cache:RedisConnectionString"]
    ?? builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConn))
{
    builder.Services.AddStackExchangeRedisCache(o =>
    {
        o.Configuration = redisConn;
        o.InstanceName = "stocky:";
    });
}
else
{
    builder.Services.AddDistributedMemoryCache();
}
builder.Services.AddSingleton<IProviderCache, ProviderCache>();

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
builder.Services.AddScoped<PortfolioAnalyticsService>();
builder.Services.AddScoped<WashSaleService>();
builder.Services.AddScoped<RebalanceService>();
builder.Services.AddHostedService<QuoteRefresher>();
builder.Services.AddHostedService<SnapshotJob>();

// M8 — Data Providers & Real-Time
builder.Services.AddScoped<IExtendedMarketDataProvider, StubExtendedMarketDataProvider>();
builder.Services.AddSignalR();
builder.Services.AddSingleton<PriceTickBroadcaster>();

// M9 — Advanced Analytics & Charts
builder.Services.AddScoped<IAdvancedMarketDataProvider, StubAdvancedMarketDataProvider>();
builder.Services.AddScoped<RiskMetricsService>();
builder.Services.AddScoped<BenchmarkComparisonService>();
builder.Services.AddScoped<BacktestService>();
builder.Services.AddScoped<GoalsService>();
builder.Services.AddSingleton<EarningsSurpriseService>();

// M10 — Advanced Alerts
builder.Services.AddSingleton<TechnicalIndicatorService>();
builder.Services.AddScoped<IInsiderTradeProvider, StubInsiderTradeProvider>();
builder.Services.AddScoped<IAlertChannel, InboxChannel>();
builder.Services.AddScoped<IAlertChannel, EmailChannel>();
builder.Services.AddScoped<IAlertChannel, PushChannel>();
builder.Services.AddScoped<IAlertChannel, WebhookChannel>();
builder.Services.AddHttpClient("stocky-webhook", c => c.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddScoped<AlertDispatcher>();
builder.Services.AddScoped<TechnicalAlertEvaluator>();
builder.Services.AddScoped<EarningsAlertEvaluator>();
builder.Services.AddScoped<NewsAlertEvaluator>();
builder.Services.AddScoped<DriftAlertEvaluator>();
builder.Services.AddScoped<InsiderAlertEvaluator>();
builder.Services.AddHostedService<AlertSweepJob>();

// M11 reporting & sharing
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ShareTokenService>();
builder.Services.AddScoped<ReportRenderer>();
builder.Services.AddHostedService<ReportScheduleJob>();

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

// Tag traces with stocky.owner_hash / stocky.portfolio_id / stocky.symbol so
// App Insights customDimensions can be filtered per user/portfolio/symbol.
app.UseMiddleware<TelemetryEnricherMiddleware>();

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
app.MapHub<PricesHub>("/hubs/prices");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<StockyDbContext>();
    if (db.Database.IsRelational())
    {
        await db.Database.MigrateAsync();
    }
}

app.Run();
