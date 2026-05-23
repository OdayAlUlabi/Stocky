using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Stocky.Api.Tests.Integration;

/// <summary>
/// Integration tests for the transactions sub-resource.
/// Verifies that posting a BUY transaction causes the corresponding holding to appear.
/// </summary>
public class TransactionsIntegrationTests(StockyApiFactory factory) : IClassFixture<StockyApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Post_BuyTransaction_CreatesHolding()
    {
        // Create a portfolio to transact against.
        var portfolioId = await CreatePortfolioAsync("Transactions Test");

        // Record a BUY transaction.
        var txResponse = await _client.PostAsJsonAsync(
            $"/api/portfolios/{portfolioId}/transactions",
            new
            {
                type = "Buy",
                symbol = "AAPL",
                quantity = 10m,
                price = 150m,
                executedAt = DateTimeOffset.UtcNow,
                currency = "USD"
            });

        Assert.Equal(HttpStatusCode.Created, txResponse.StatusCode);

        // Verify the holding was created.
        var holdingsResponse = await _client.GetAsync($"/api/portfolios/{portfolioId}/holdings");
        Assert.Equal(HttpStatusCode.OK, holdingsResponse.StatusCode);

        var holdings = await holdingsResponse.Content.ReadFromJsonAsync<List<HoldingResponse>>();
        Assert.NotNull(holdings);

        var appleHolding = holdings.SingleOrDefault(h => h.Symbol == "AAPL");
        Assert.NotNull(appleHolding);
        Assert.Equal(10m, appleHolding.Quantity);
    }

    [Fact]
    public async Task Post_SellTransaction_ReducesHolding()
    {
        var portfolioId = await CreatePortfolioAsync("Sell Test");

        // Buy 20 shares first.
        await PostTransactionAsync(portfolioId, "Buy", "MSFT", 20m, 300m);

        // Sell 5 shares.
        var sellResponse = await _client.PostAsJsonAsync(
            $"/api/portfolios/{portfolioId}/transactions",
            new
            {
                type = "Sell",
                symbol = "MSFT",
                quantity = 5m,
                price = 310m,
                executedAt = DateTimeOffset.UtcNow,
                currency = "USD"
            });

        Assert.Equal(HttpStatusCode.Created, sellResponse.StatusCode);

        var holdingsResponse = await _client.GetAsync($"/api/portfolios/{portfolioId}/holdings");
        var holdings = await holdingsResponse.Content.ReadFromJsonAsync<List<HoldingResponse>>();
        var msft = holdings?.SingleOrDefault(h => h.Symbol == "MSFT");

        Assert.NotNull(msft);
        Assert.Equal(15m, msft.Quantity);
    }

    [Fact]
    public async Task Post_Transaction_Returns400_ForInvalidType()
    {
        var portfolioId = await CreatePortfolioAsync("BadType Test");

        var response = await _client.PostAsJsonAsync(
            $"/api/portfolios/{portfolioId}/transactions",
            new { type = "INVALID", symbol = "AAPL", quantity = 1m, price = 100m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_Transactions_Returns404_ForUnknownPortfolio()
    {
        var response = await _client.GetAsync($"/api/portfolios/{Guid.NewGuid()}/transactions");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<Guid> CreatePortfolioAsync(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/portfolios",
            new { name, baseCurrency = "USD" });
        response.EnsureSuccessStatusCode();
        var portfolio = await response.Content.ReadFromJsonAsync<PortfolioIdHolder>();
        return portfolio?.Id ?? throw new InvalidOperationException("No Id in portfolio response");
    }

    private async Task PostTransactionAsync(
        Guid portfolioId, string type, string symbol, decimal quantity, decimal price)
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/portfolios/{portfolioId}/transactions",
            new { type, symbol, quantity, price, executedAt = DateTimeOffset.UtcNow, currency = "USD" });
        response.EnsureSuccessStatusCode();
    }

    private sealed record PortfolioIdHolder(Guid Id);

    private sealed record HoldingResponse(Guid Id, string Symbol, decimal Quantity, decimal AverageCost,
        decimal? LatestPrice, decimal? MarketValue);
}
