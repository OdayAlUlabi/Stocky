using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Stocky.Api.Tests.Integration;

/// <summary>
/// Integration tests for API authentication behaviour.
/// </summary>
public class AuthIntegrationTests
{
    // ── Default factory (TestAuthHandler active) ──────────────────────────────

    [Fact]
    public async Task Authenticated_Endpoint_Returns200_WithTestAuthHandler()
    {
        await using var factory = new StockyApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/portfolios");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_Endpoint_Is_Anonymous_Returns200()
    {
        await using var factory = new StockyApiFactory();
        var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── No-auth factory: no test handler — raw 401 behaviour ─────────────────

    [Fact]
    public async Task Protected_Endpoint_Returns401_WhenNoAuthSchemeActive()
    {
        await using var noAuthFactory = new NoAuthFactory();
        var client = noAuthFactory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/portfolios");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── NoAuthFactory: like StockyApiFactory but without TestAuthHandler ─────

    private sealed class NoAuthFactory : StockyApiFactory
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            // Inherit DB isolation, background-service removal, and CORS config.
            base.ConfigureWebHost(builder);

            // Remove the TestAuthHandler's PostConfigure override so no automatic
            // authentication happens and [Authorize] endpoints return 401.
            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<Microsoft.AspNetCore.Authentication.AuthenticationOptions>(opts =>
                {
                    opts.DefaultScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
                    opts.DefaultAuthenticateScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
                    opts.DefaultChallengeScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
                });
            });
        }
    }
}

