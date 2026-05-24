using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.EntityFrameworkCore;
using Stocky.Api;

var builder = WebApplication.CreateBuilder(args);

// -----------------------------------------------------------------------------
// MVC + Razor Views
// -----------------------------------------------------------------------------
builder.Services.AddControllersWithViews();

// -----------------------------------------------------------------------------
// Auth — cookie sign-in with optional Google OIDC challenge.
//
// Design: we use cookie auth as the SignInScheme so HTML pages get a stable
// session cookie after the OIDC dance. The cookie carries the Google `sub`
// claim — UserContextExtensions.GetOwnerId() already reads `sub` first, so
// rows owned by SPA users remain accessible after the cutover.
//
// When Google is not configured (e.g. local dev without secrets) the app still
// boots; protected endpoints will 401 until you wire credentials. See
// docs/non-spa-rearchitecture.md §4.4.
// -----------------------------------------------------------------------------
var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];

var authBuilder = builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Account/Login";
        o.LogoutPath = "/Account/Logout";
        o.AccessDeniedPath = "/Account/Login";
        o.ExpireTimeSpan = TimeSpan.FromDays(7);
        o.SlidingExpiration = true;
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.Cookie.Name = ".Stocky.Auth";
    });

if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    authBuilder.AddGoogle(GoogleDefaults.AuthenticationScheme, o =>
    {
        o.ClientId = googleClientId;
        o.ClientSecret = googleClientSecret;
        o.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        // Persist the Google `sub` claim verbatim so GetOwnerId() finds it.
        o.ClaimActions.MapJsonKey("sub", "sub");
        o.SaveTokens = false;
    });
}

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

// Anonymous health probe so ACA liveness/readiness can succeed without auth.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .AllowAnonymous();

app.Run();
