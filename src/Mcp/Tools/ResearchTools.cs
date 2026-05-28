using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Stocky.Mcp.Tools;

/// <summary>
/// MCP tools for market research: screener, earnings, news, correlation, and alerts.
/// </summary>
[McpServerToolType]
public sealed class ResearchTools(IHttpClientFactory http)
{
    private static readonly JsonSerializerOptions PrettyJson =
        new() { WriteIndented = true };

    private HttpClient Api => http.CreateClient("StockyApi");

    [McpServerTool]
    [Description(
        "Screen stocks/ETFs by fundamental criteria. Supports filtering by asset class, sector, " +
        "industry, country, market-cap range, and beta range. Returns matching instruments " +
        "with their latest price.")]
    public async Task<string> ScreenSecurities(
        [Description("Asset class filter: Stocks | ETF | Crypto (optional).")] string? assetClass = null,
        [Description("Sector filter, e.g. 'Technology' (optional).")] string? sector = null,
        [Description("Industry filter, e.g. 'Semiconductors' (optional).")] string? industry = null,
        [Description("Country filter, e.g. 'US' (optional).")] string? country = null,
        [Description("Minimum market cap in USD (optional).")] decimal? minMarketCap = null,
        [Description("Maximum market cap in USD (optional).")] decimal? maxMarketCap = null,
        [Description("Minimum beta (optional).")] decimal? minBeta = null,
        [Description("Maximum beta (optional).")] decimal? maxBeta = null,
        [Description("Max number of results to return (default 25).")] int limit = 25,
        CancellationToken ct = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(assetClass)) query.Add($"assetClass={Uri.EscapeDataString(assetClass)}");
        if (!string.IsNullOrWhiteSpace(sector)) query.Add($"sector={Uri.EscapeDataString(sector)}");
        if (!string.IsNullOrWhiteSpace(industry)) query.Add($"industry={Uri.EscapeDataString(industry)}");
        if (!string.IsNullOrWhiteSpace(country)) query.Add($"country={Uri.EscapeDataString(country)}");
        if (minMarketCap.HasValue) query.Add($"minMarketCap={minMarketCap}");
        if (maxMarketCap.HasValue) query.Add($"maxMarketCap={maxMarketCap}");
        if (minBeta.HasValue) query.Add($"minBeta={minBeta}");
        if (maxBeta.HasValue) query.Add($"maxBeta={maxBeta}");
        query.Add($"limit={limit}");

        var url = "api/screener?" + string.Join("&", query);
        var resp = await Api.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        return JsonSerializer.Serialize(json, PrettyJson);
    }

    [McpServerTool]
    [Description(
        "Return recent earnings announcements and EPS estimates for the symbols currently held " +
        "in a portfolio, or for a specific list of symbols.")]
    public async Task<string> GetEarnings(
        [Description("Portfolio GUID to get earnings for all current holdings (use this or symbols).")] string? portfolioId = null,
        [Description("Comma-separated ticker symbols, e.g. 'AAPL,MSFT' (use this or portfolioId).")] string? symbols = null,
        CancellationToken ct = default)
    {
        string url;
        if (!string.IsNullOrWhiteSpace(portfolioId))
            url = $"api/portfolios/{Uri.EscapeDataString(portfolioId)}/earnings";
        else if (!string.IsNullOrWhiteSpace(symbols))
            url = $"api/earnings?symbols={Uri.EscapeDataString(symbols.ToUpperInvariant())}";
        else
            return "Error: provide portfolioId or symbols.";

        var resp = await Api.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        return JsonSerializer.Serialize(json, PrettyJson);
    }

    [McpServerTool]
    [Description(
        "Return the return-correlation matrix for the holdings in a portfolio. " +
        "Useful for understanding diversification and concentration risk.")]
    public async Task<string> GetCorrelationMatrix(
        [Description("Portfolio GUID.")] string portfolioId,
        [Description("Look-back period in trading days (default 252 = 1 year).")] int days = 252,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(portfolioId))
            return "Error: portfolioId is required.";

        var resp = await Api.GetAsync(
            $"api/portfolios/{Uri.EscapeDataString(portfolioId)}/correlation?days={days}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            return $"Portfolio {portfolioId} not found.";
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        return JsonSerializer.Serialize(json, PrettyJson);
    }

    [McpServerTool]
    [Description("List all active price alerts for a portfolio.")]
    public async Task<string> GetAlerts(
        [Description("Portfolio GUID.")] string portfolioId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(portfolioId))
            return "Error: portfolioId is required.";

        var resp = await Api.GetAsync(
            $"api/portfolios/{Uri.EscapeDataString(portfolioId)}/alerts", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            return $"Portfolio {portfolioId} not found.";
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        return JsonSerializer.Serialize(json, PrettyJson);
    }

    [McpServerTool]
    [Description(
        "Return the latest financial news headlines for the symbols held in a portfolio " +
        "or for an explicit list of tickers.")]
    public async Task<string> GetNews(
        [Description("Portfolio GUID (use this or symbols).")] string? portfolioId = null,
        [Description("Comma-separated ticker symbols (use this or portfolioId).")] string? symbols = null,
        [Description("Max headlines to return (default 20).")] int limit = 20,
        CancellationToken ct = default)
    {
        string url;
        if (!string.IsNullOrWhiteSpace(portfolioId))
            url = $"api/portfolios/{Uri.EscapeDataString(portfolioId)}/news?limit={limit}";
        else if (!string.IsNullOrWhiteSpace(symbols))
            url = $"api/news?symbols={Uri.EscapeDataString(symbols.ToUpperInvariant())}&limit={limit}";
        else
            return "Error: provide portfolioId or symbols.";

        var resp = await Api.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        return JsonSerializer.Serialize(json, PrettyJson);
    }
}
