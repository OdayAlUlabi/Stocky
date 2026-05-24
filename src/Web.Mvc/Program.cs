using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Stocky.Api;
using Stocky.Web.Mvc.Internal;

var builder = WebApplication.CreateBuilder(args);

// -----------------------------------------------------------------------------
// MVC + Razor Views
// -----------------------------------------------------------------------------
builder.Services.AddControllersWithViews();

// -----------------------------------------------------------------------------
// Auth — single-user passthrough. Google OAuth has been removed; every
// request is signed in as a fixed local identity by AutoAuthenticationHandler
// so existing [Authorize] attributes and owner-scoped queries keep working.
// Override the owner id via the Auth:LocalOwnerId config key to keep
// previously-owned rows visible.
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
builder.Services.AddDbContext<Stocky.Api.Data.StockyDbContext>(options =>
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

builder.Services.AddStockyDomainServices(builder.Configuration);

var app = builder.Build();

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
