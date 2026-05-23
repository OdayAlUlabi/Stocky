using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Stocky.Api.Data;

namespace Stocky.Api.Tests.Integration;

/// <summary>
/// Spins up an in-process test server against the real ASP.NET Core pipeline.
///
/// Environment: "Development"
///   → appsettings.Development.json is loaded: Google:ClientId and AllowedOrigins
///     are present, satisfying both startup validation guards in Program.cs.
///
/// Authentication: <see cref="TestAuthHandler"/> is registered as the default
///   authentication scheme via PostConfigure so it wins over all other registrations
///   and auto-authenticates every request as oid=00000000-0000-0000-0000-000000000001.
///
/// Database: A unique per-factory-instance InMemoryDatabase replaces the real DB
///   so parallel test classes don't bleed data into each other.
///
/// Background services: all IHostedService registrations are removed so
///   QuoteRefresher, SnapshotJob, etc. don't race against test data.
/// </summary>
public class StockyApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"stocky-test-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development: appsettings.Development.json provides Google:ClientId and AllowedOrigins,
        // satisfying both non-Development startup guards in Program.cs.
        builder.UseEnvironment("Development");

        // ConfigureTestServices runs AFTER Program.cs's ConfigureServices calls.
        builder.ConfigureTestServices(services =>
        {
            // ── Database ─────────────────────────────────────────────────────────
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<StockyDbContext>));
            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddDbContext<StockyDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));

            // ── Background services ───────────────────────────────────────────────
            var hostedServices = services
                .Where(d => d.ServiceType == typeof(IHostedService))
                .ToList();
            foreach (var s in hostedServices)
                services.Remove(s);

            // ── Authentication ────────────────────────────────────────────────────
            // Add a test handler that auto-authenticates every request.
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });

            // PostConfigure runs after ALL Configure callbacks — guaranteed last-write-wins.
            services.PostConfigure<AuthenticationOptions>(opts =>
            {
                opts.DefaultScheme = TestAuthHandler.SchemeName;
                opts.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                opts.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            });
        });
    }

    /// <summary>Resolves a scoped service from the test server's DI container.</summary>
    public T GetRequiredService<T>() where T : notnull =>
        Services.GetRequiredService<T>();
}
