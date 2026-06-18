using Microsoft.AspNetCore.Authentication;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.EntityFrameworkCore;
using Stocky.Api;
using Stocky.Web.Mvc.Internal;

var builder = WebApplication.CreateBuilder(args);

// -----------------------------------------------------------------------------
// MVC + Razor Views
// -----------------------------------------------------------------------------
builder.Services.AddControllersWithViews();

// -----------------------------------------------------------------------------
// Auth — single-user passthrough via AutoAuthenticationHandler.
// Every request is signed in as a fixed local identity so [Authorize] attributes
// and owner-scoped queries keep working.
// Override the owner id via the Auth:LocalOwnerId config key.
// -----------------------------------------------------------------------------
builder.Services
    .AddAuthentication(AutoAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, AutoAuthenticationHandler>(
        AutoAuthenticationHandler.SchemeName, _ => { });

builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(o => o.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddDistributedMemoryCache();

// -----------------------------------------------------------------------------
// Shared domain services + DbContext (same wiring as src/Api/Program.cs, via
// StockyServicesExtensions.AddStockyDomainServices). The MVC controllers below
// resolve the existing src/Api REST controllers in-process and forward their
// payloads to Razor views — see Internal/ApiControllerInvoker.cs.
// -----------------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("Sql")
    ?? builder.Configuration["Sql:ConnectionString"];

// SQL auth via DbConnectionInterceptor (same wiring as src/Api/Program.cs).
// Connection string in prod has no User ID / Authentication — token is
// injected per-connection by SqlTokenInterceptor (with MSAL caching) so EF
// Core's retry storms don't hammer the ACA IMDS proxy.
SqlAuthSetup.Register(builder, connectionString);

builder.Services.AddDbContext<Stocky.Api.Data.StockyDbContext>((sp, options) =>
{
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        var connStr = SqlAuthSetup.StripAuthFromConnectionString(connectionString);
        options.UseSqlServer(connStr, sql => sql.EnableRetryOnFailure());
        var interceptor = sp.GetService<SqlTokenInterceptor>();
        if (interceptor != null)
            options.AddInterceptors(interceptor);
    }
    else
    {
        options.UseInMemoryDatabase("stocky-dev");
    }
});

builder.Services.AddStockyDomainServices(builder.Configuration);

var appInsightsConnection = builder.Configuration["ApplicationInsights:ConnectionString"]
    ?? builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrWhiteSpace(appInsightsConnection))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor(o => o.ConnectionString = appInsightsConnection);
}

// IProviderCache is registered in Api/Program.cs alongside the Redis /
// in-memory IDistributedCache setup. We need it here too so that in-process
// invocations of API controllers (via ApiControllerInvoker) that depend on
// IMarketDataProvider → AlpacaMarketDataProvider can resolve their cache
// dependency. Uses the AddDistributedMemoryCache registered above.
builder.Services.AddSingleton<Stocky.Api.Services.IProviderCache, Stocky.Api.Services.ProviderCache>();

var app = builder.Build();

// Warm up SQL credential before serving traffic (ACA IMDS cold-start mitigation).
await SqlAuthSetup.WarmupAsync(app);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Note: ACA liveness/readiness `/health` is served by
// Stocky.Api.Controllers.HealthController (loaded via the Api project
// reference). A duplicate MapGet here causes AmbiguousMatchException.

app.Run();
