using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Stocky.Api.Data;
using Stocky.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// --migrate-only: short-circuit startup. Used by the ACA migrator job so the
// API image doubles as the migration tool — no separate artifact required.
var migrateOnly = args.Contains("--migrate-only", StringComparer.OrdinalIgnoreCase);

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

if (migrateOnly)
{
    using var migrateApp = builder.Build();
    using var scope = migrateApp.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<StockyDbContext>();
    if (db.Database.IsRelational())
    {
        // Retry loop: IMDS / managed identity token service in ACA can return
        // transient 500s during container warm-up. Retry with backoff.
        var maxAttempts = 10;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync();
                Console.WriteLine("Migrations applied.");
                break;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                Console.WriteLine($"Migration attempt {attempt}/{maxAttempts} failed: {ex.Message}");
                Console.WriteLine($"Retrying in {attempt * 10} seconds...");
                await Task.Delay(TimeSpan.FromSeconds(attempt * 10));
                // Reset connection state for next attempt
                await db.Database.CloseConnectionAsync();
            }
        }
    }
    else
    {
        Console.WriteLine("Skipping migrations: non-relational provider.");
    }
    return;
}

var entraClientIdAtStartup = builder.Configuration["AzureAd:ClientId"];

// Production hardening: refuse to start outside Development without a configured
// Entra audience. Prevents accidental deploys where the dev-bypass middleware
// would otherwise inject a synthetic authenticated principal.
if (!builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(entraClientIdAtStartup))
{
    throw new InvalidOperationException(
        "AzureAd:ClientId must be configured outside the Development environment. " +
        "Refusing to start with anonymous-by-default authentication.");
}

var authBuilder = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme);
if (!string.IsNullOrWhiteSpace(entraClientIdAtStartup))
{
    authBuilder.AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
}
else
{
    // Register a no-op JwtBearer handler so UseAuthentication() can resolve
    // the default scheme even when Entra isn't configured (dev / tests).
    authBuilder.AddJwtBearer(_ => { });
}
// M14 #91 — API-key bearer scheme (sk_*) for /v1/public endpoints
authBuilder.AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
    ApiKeyAuthenticationHandler.SchemeName, _ => { });

builder.Services.AddAuthorization();
builder.Services.AddScoped<ApiKeyService>();

// M14 #91 — per-key rate limit: 60 req/min/key, 1000 req/min/IP fallback.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("api-key", httpContext =>
    {
        var keyId = httpContext.User.FindFirst("apikey_id")?.Value
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "anon";
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            keyId,
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });
    // Public share resolver: per-IP throttle so the hashed-token store can't
    // be enumerated. 30 req/min/IP.
    o.AddPolicy("public-share", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon";
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            ip,
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });
});
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
builder.Services.AddSingleton<PortfolioUpdatedBroadcaster>();

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

// M14 platform & admin
builder.Services.AddScoped<AuditLogger>();
builder.Services.AddScoped<CashService>();

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
            if (!builder.Environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "AllowedOrigins must be configured outside Development.");
            }
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
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Baseline security response headers (applied to every response).
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["Referrer-Policy"] = "no-referrer";
    h["X-Frame-Options"] = "DENY";
    h["Cross-Origin-Opener-Policy"] = "same-origin";
    h["Cross-Origin-Resource-Policy"] = "same-site";
    await next();
});

app.UseCors(CorsPolicy);

// Tag traces with stocky.owner_hash / stocky.portfolio_id / stocky.symbol so
// App Insights customDimensions can be filtered per user/portfolio/symbol.
app.UseMiddleware<TelemetryEnricherMiddleware>();

// Always run authentication so the ApiKey scheme can authorize /v1/public requests
// even when Entra is not configured.
app.UseAuthentication();

// Dev-only auth bypass: when Entra isn't configured AND the request didn't already
// authenticate via an API key, inject a synthetic user so the UI is browsable.
var entraClientId = builder.Configuration["AzureAd:ClientId"];
if (app.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(entraClientId))
{
    app.Use(async (ctx, next) =>
    {
        if (ctx.User?.Identity?.IsAuthenticated != true)
        {
            var identity = new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim("oid", "00000000-0000-0000-0000-000000000001"),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "Local Dev User")
            }, "DevBypass");
            ctx.User = new System.Security.Claims.ClaimsPrincipal(identity);
        }
        await next();
    });
}
app.UseAuthorization();
app.UseRateLimiter();
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
