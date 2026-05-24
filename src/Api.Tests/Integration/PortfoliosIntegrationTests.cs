using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Stocky.Api.Tests.Integration;

/// <summary>
/// Integration tests for POST/GET/PUT/DELETE /api/portfolios.
/// All requests go through the full ASP.NET pipeline with the dev-bypass auth middleware
/// injecting oid = "00000000-0000-0000-0000-000000000001" as the authenticated user.
/// </summary>
public class PortfoliosIntegrationTests(StockyApiFactory factory) : IClassFixture<StockyApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    // ── Create ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_CreatesPortfolio_Returns201WithBody()
    {
        var response = await _client.PostAsJsonAsync("/api/portfolios",
            new { name = "Tech Focus", baseCurrency = "USD" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<PortfolioResponse>();
        Assert.NotNull(created);
        Assert.Equal("Tech Focus", created.Name);
        Assert.Equal("USD", created.BaseCurrency);
        Assert.NotEqual(Guid.Empty, created.Id);
    }

    [Fact]
    public async Task Post_DefaultsBaseCurrencyToUSD_WhenOmitted()
    {
        var response = await _client.PostAsJsonAsync("/api/portfolios",
            new { name = "Default Currency" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<PortfolioResponse>();
        Assert.Equal("USD", created?.BaseCurrency);
    }

    [Fact]
    public async Task Post_Returns400_WhenNameMissing()
    {
        var response = await _client.PostAsJsonAsync("/api/portfolios",
            new { baseCurrency = "USD" });

        // ASP.NET model validation returns 400 when Name is missing.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns400ValidationProblem_WhenNameTooLong()
    {
        var response = await _client.PostAsJsonAsync("/api/portfolios",
            new { name = new string('A', 121), baseCurrency = "USD" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Name", body);
        Assert.Contains("120", body);
    }

    // ── Read ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_ById_ReturnsCreatedPortfolio()
    {
        var created = await CreatePortfolioAsync("GetById Test");

        var response = await _client.GetAsync($"/api/portfolios/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fetched = await response.Content.ReadFromJsonAsync<PortfolioResponse>();
        Assert.Equal(created.Id, fetched?.Id);
        Assert.Equal("GetById Test", fetched?.Name);
    }

    [Fact]
    public async Task Get_ById_Returns404_ForNonexistentId()
    {
        var response = await _client.GetAsync($"/api/portfolios/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_List_ContainsAllCreatedPortfolios()
    {
        var a = await CreatePortfolioAsync("ListPortfolio_A");
        var b = await CreatePortfolioAsync("ListPortfolio_B");

        var response = await _client.GetAsync("/api/portfolios");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<List<PortfolioResponse>>();
        Assert.NotNull(list);
        Assert.Contains(list, p => p.Id == a.Id);
        Assert.Contains(list, p => p.Id == b.Id);
    }

    // ── Update ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Put_UpdatesPortfolioName_Returns200()
    {
        var created = await CreatePortfolioAsync("Original Name");

        var response = await _client.PutAsJsonAsync($"/api/portfolios/{created.Id}",
            new { name = "Renamed", baseCurrency = "USD", costBasisMethod = "Fifo" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<PortfolioResponse>();
        Assert.Equal("Renamed", updated?.Name);
    }

    [Fact]
    public async Task Put_Returns404_ForNonexistentId()
    {
        var response = await _client.PutAsJsonAsync($"/api/portfolios/{Guid.NewGuid()}",
            new { name = "Ghost", baseCurrency = "USD", costBasisMethod = "Fifo" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_RemovesPortfolio_Returns204_ThenGetReturns404()
    {
        var created = await CreatePortfolioAsync("ToDelete");

        var deleteResponse = await _client.DeleteAsync($"/api/portfolios/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await _client.GetAsync($"/api/portfolios/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Delete_Returns404_ForNonexistentId()
    {
        var response = await _client.DeleteAsync($"/api/portfolios/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<PortfolioResponse> CreatePortfolioAsync(string name, string currency = "USD")
    {
        var response = await _client.PostAsJsonAsync("/api/portfolios",
            new { name, baseCurrency = currency });
        response.EnsureSuccessStatusCode();
        var portfolio = await response.Content.ReadFromJsonAsync<PortfolioResponse>();
        return portfolio ?? throw new InvalidOperationException("Empty body from POST /api/portfolios");
    }

    private sealed record PortfolioResponse(
        Guid Id,
        string Name,
        string BaseCurrency,
        DateTimeOffset CreatedAt,
        decimal CashBalance,
        string CostBasisMethod);
}
