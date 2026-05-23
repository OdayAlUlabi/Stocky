using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Stocky.Api.Tests.Integration;

/// <summary>
/// Integration tests for the /health endpoint.
/// Health is unauthenticated — verifies it responds 200 with no credentials.
/// </summary>
public class HealthIntegrationTests(StockyApiFactory factory) : IClassFixture<StockyApiFactory>
{
    [Fact]
    public async Task Get_Health_Returns200_WithoutAuth()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.Equal("ok", body?.Status);
    }

    private sealed record HealthResponse(string Status, DateTimeOffset Utc);
}
