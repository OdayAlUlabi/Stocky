using Azure.Core;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Azure.Security.KeyVault.Secrets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Stocky.Api.Data;
using Stocky.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// --migrate-only: short-circuit startup. Used by the ACA migrator job so the
// API image doubles as the migration tool — no separate artifact required.
var migrateOnly = args.Contains("--migrate-only", StringComparer.OrdinalIgnoreCase);

var connectionString = builder.Configuration.GetConnectionString("Sql")
    ?? builder.Configuration["Sql:ConnectionString"];

// When a SQL connection string uses ActiveDirectoryManagedIdentity auth, SqlClient calls
// the ACA IMDS proxy on EVERY new SqlConnection. The ACA IMDS proxy throttles under burst
// (e.g. EnableRetryOnFailure replaying connections during SQL Serverless cold-start) and
// returns HTTP 500. Fix: strip the auth keyword from the connection string and inject the
// token via a DbConnectionInterceptor instead. Azure.Identity caches the token (~55 min)
// so only the first connection ever hits IMDS.
if (!string.IsNullOrWhiteSpace(connectionString) && !migrateOnly)
{
    var azureClientId = builder.Configuration["AZURE_CLIENT_ID"];
    var spClientId = builder.Configuration["Sql:SpClientId"];
    var spTenantId = builder.Configuration["Sql:TenantId"];
    var kvUri = builder.Configuration["KeyVaultUri"];

    TokenCredential sqlCredential;
    if (!string.IsNullOrWhiteSpace(spClientId) && !string.IsNullOrWhiteSpace(spTenantId)
        && !string.IsNullOrWhiteSpace(kvUri))
    {
        // Use managed identity to fetch the SP cert from Key Vault, then authenticate
        // to SQL as the service principal — eliminates IMDS dependency for SQL tokens.
        // Retry: the ACA IMDS proxy can be unavailable for several seconds at container
        // cold-start, causing ManagedIdentityCredential to throw on the first attempt.
        TokenCredential kvCredential = string.IsNullOrWhiteSpace(azureClientId)
            ? new DefaultAzureCredential()
            : new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(azureClientId));
        var secretClient = new SecretClient(new Uri(kvUri), kvCredential);
        KeyVaultSecret certSecret = null!;
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            try
            {
                certSecret = await secretClient.GetSecretAsync("stocky-api-sql-cert");
                break;
            }
            catch (Exception ex) when (attempt < 10)
            {
                Console.WriteLine($"KV/IMDS attempt {attempt}/10 failed: {ex.Message}. Retrying in 10s...");
                await Task.Delay(10_000);
            }
        }
        var certBytes = Convert.FromBase64String(certSecret.Value.Value);
        var certificate = X509CertificateLoader.LoadPkcs12(
            certBytes, (string?)null,
            X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
        sqlCredential = new ClientCertificateCredential(spTenantId, spClientId, certificate);
    }
    else
    {
        sqlCredential = string.IsNullOrWhiteSpace(azureClientId)
            ? new DefaultAzureCredential()
            : new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(azureClientId));
    }
    builder.Services.AddSingleton<TokenCredential>(sqlCredential);
    builder.Services.AddSingleton<SqlTokenInterceptor>();
}

builder.Services.AddDbContext<StockyDbContext>((sp, options) =>
{
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        // Strip ActiveDirectoryManagedIdentity auth — token is injected by SqlTokenInterceptor.
        var csb = new SqlConnectionStringBuilder(connectionString);
        csb.Authentication = SqlAuthenticationMethod.NotSpecified;
        csb.Remove("User ID");
        var connStrWithoutAuth = csb.ConnectionString;

        options.UseSqlServer(connStrWithoutAuth, sql => sql.EnableRetryOnFailure());
        var interceptor = sp.GetService<SqlTokenInterceptor>();
        if (interceptor != null)
            options.AddInterceptors(interceptor);
    }
    else
    {
        options.UseInMemoryDatabase("stocky-dev");
    }
});

if (migrateOnly)
{
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        string? accessToken = null;

        // Fast path: use a SQL Bearer token pre-fetched by the CI runner and
        // injected via az containerapp job start --env-vars PRE_FETCHED_SQL_TOKEN=...
        // This bypasses the ACA IMDS proxy entirely (the proxy is unreliable for
        // the migrator UAMI on some compute nodes).
        // Obtain the token on the CI side with:
        //   az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv
        // The CICD identity (OIDC) must be a SQL db_owner — run once as admin:
        //   CREATE USER [id-stocky-prod-cicd] FROM EXTERNAL PROVIDER;
        //   ALTER ROLE db_owner ADD MEMBER [id-stocky-prod-cicd];
        var preFetchedToken = Environment.GetEnvironmentVariable("PRE_FETCHED_SQL_TOKEN");
        if (!string.IsNullOrWhiteSpace(preFetchedToken))
        {
            accessToken = preFetchedToken;
            Console.WriteLine("SQL token: using PRE_FETCHED_SQL_TOKEN (IMDS skipped).");
        }
        else
        {
            // Slow path: acquire the token from the ACA IMDS proxy.
            // SqlClient's ActiveDirectoryManagedIdentity auth calls IMDS on every new
            // SqlConnection; when EnableRetryOnFailure() retries due to error 40613
            // (SQL Serverless auto-resume), this triggers dozens of rapid IMDS calls
            // that throttle the ACA IMDS proxy (HTTP 500). Passing AccessToken directly
            // on the SqlConnection bypasses per-connection IMDS entirely.
            var clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
            TokenCredential credential = string.IsNullOrEmpty(clientId)
                ? new DefaultAzureCredential()
                : new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId));

            for (var i = 1; i <= 20; i++)
            {
                try
                {
                    var t = await credential.GetTokenAsync(
                        new TokenRequestContext(["https://database.windows.net/.default"]),
                        CancellationToken.None);
                    accessToken = t.Token;
                    Console.WriteLine($"MI token acquired (expires {t.ExpiresOn:u}).");
                    break;
                }
                catch (Exception ex) when (i < 20)
                {
                    Console.WriteLine($"IMDS attempt {i}/20 failed: {ex.Message}. Retrying in 10s...");
                    await Task.Delay(10_000);
                }
            }

            if (accessToken == null)
            {
                Console.Error.WriteLine("Could not acquire MI token after 20 attempts. Aborting.");
                Environment.Exit(1);
            }
        }

        // SqlConnection.AccessToken and Authentication= are mutually exclusive;
        // strip auth keywords from the connection string before setting the token.
        var csb = new SqlConnectionStringBuilder(connectionString);
        csb.Authentication = SqlAuthenticationMethod.NotSpecified;
        csb.Remove("User ID");
        using var sqlConn = new SqlConnection(csb.ConnectionString) { AccessToken = accessToken };

        var dbOptions = new DbContextOptionsBuilder<StockyDbContext>()
            .UseSqlServer(sqlConn, sql => sql.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null))
            .Options;
        await using var db = new StockyDbContext(dbOptions);
        await db.Database.MigrateAsync();
        Console.WriteLine("Migrations applied.");
    }
    else
    {
        Console.WriteLine("Skipping migrations: no SQL connection string configured.");
    }
    return;
}

var googleClientId = builder.Configuration["Google:ClientId"];

var authBuilder = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme);
if (!string.IsNullOrWhiteSpace(googleClientId))
{
    authBuilder.AddJwtBearer(options =>
    {
        options.Authority = "https://accounts.google.com";
        options.Audience = googleClientId;
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = ["accounts.google.com", "https://accounts.google.com"],
            ValidateAudience = true,
            ValidAudience = googleClientId
        };
    });
}
else
{
    // No-op bearer so UseAuthentication() can resolve the default scheme in dev.
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

// Dev-only auth bypass: when Google OAuth isn't configured AND the request didn't already
// authenticate via an API key, inject a synthetic user so the UI is browsable.
if (app.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(googleClientId))
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
        try
        {
            await db.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            var startupLogger = scope.ServiceProvider
                .GetRequiredService<ILogger<StockyDbContext>>();
            startupLogger.LogWarning(ex,
                "Startup migration check failed — proceeding without migration. " +
                "Ensure the migrator job has run before serving traffic.");
        }
    }
}

app.Run();

// Injects an Azure AD access token on every SqlConnection before it is opened,
// bypassing SqlClient's per-connection IMDS call which can throttle the ACA proxy.
// Azure.Identity caches the token (~55 min), so only the first call ever hits IMDS.
internal sealed class SqlTokenInterceptor(TokenCredential credential) : DbConnectionInterceptor
{
    private static readonly string[] SqlScope = ["https://database.windows.net/.default"];

    public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        if (connection is SqlConnection sqlConn && string.IsNullOrEmpty(sqlConn.AccessToken))
        {
            var token = await credential.GetTokenAsync(
                new TokenRequestContext(SqlScope), cancellationToken);
            sqlConn.AccessToken = token.Token;
        }
        return result;
    }

    public override InterceptionResult ConnectionOpening(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        if (connection is SqlConnection sqlConn && string.IsNullOrEmpty(sqlConn.AccessToken))
        {
            var token = credential.GetTokenAsync(
                new TokenRequestContext(SqlScope), CancellationToken.None).GetAwaiter().GetResult();
            sqlConn.AccessToken = token.Token;
        }
        return result;
    }
}
