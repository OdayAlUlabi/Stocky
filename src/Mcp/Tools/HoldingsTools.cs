using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Stocky.Mcp.Tools;

/// <summary>
/// MCP tools for holdings — current positions with live market values.
/// </summary>
[McpServerToolType]
public sealed class HoldingsTools(IHttpClientFactory http)
{
    private static readonly JsonSerializerOptions PrettyJson =
        new() { WriteIndented = true };

    private HttpClient Api => http.CreateClient("StockyApi");

    [McpServerTool]
    [Description(
        "List all current holdings (open positions) for a portfolio. " +
        "Returns symbol, quantity, average cost basis, latest market price, and total market value for each position.")]
    public async Task<string> GetHoldings(
        [Description("Portfolio GUID (from list_portfolios).")] string portfolioId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(portfolioId))
            return "Error: portfolioId is required.";

        var resp = await Api.GetAsync(
            $"api/portfolios/{Uri.EscapeDataString(portfolioId)}/holdings", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            return $"Portfolio {portfolioId} not found.";
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        return JsonSerializer.Serialize(json, PrettyJson);
    }

    [McpServerTool]
    [Description(
        "Get the current allocation breakdown for a portfolio: how much of the portfolio " +
        "is in each sector and asset class, expressed as absolute values and percentages.")]
    public async Task<string> GetAllocation(
        [Description("Portfolio GUID.")] string portfolioId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(portfolioId))
            return "Error: portfolioId is required.";

        var resp = await Api.GetAsync(
            $"api/portfolios/{Uri.EscapeDataString(portfolioId)}/allocation", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            return $"Portfolio {portfolioId} not found.";
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        return JsonSerializer.Serialize(json, PrettyJson);
    }

    [McpServerTool]
    [Description(
        "Get the detailed position view for a single holding: full tax-lot breakdown, " +
        "realized and unrealized P&L, wash-sale adjustments, and price history.")]
    public async Task<string> GetPositionDetail(
        [Description("Portfolio GUID.")] string portfolioId,
        [Description("Ticker symbol, e.g. 'AAPL'.")] string symbol,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(portfolioId) || string.IsNullOrWhiteSpace(symbol))
            return "Error: portfolioId and symbol are both required.";

        var resp = await Api.GetAsync(
            $"api/portfolios/{Uri.EscapeDataString(portfolioId)}/position/{Uri.EscapeDataString(symbol.ToUpperInvariant())}",
            ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            return $"No position for {symbol.ToUpperInvariant()} in portfolio {portfolioId}.";
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        return JsonSerializer.Serialize(json, PrettyJson);
    }

    [McpServerTool]
    [Description(
        "Get the latest market quote for one or more ticker symbols. " +
        "Returns price, day change, day change percent, and timestamp.")]
    public async Task<string> GetQuotes(
        [Description("Comma-separated list of ticker symbols, e.g. 'AAPL,MSFT,GOOGL'.")] string symbols,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbols))
            return "Error: symbols is required.";

        var encoded = Uri.EscapeDataString(symbols.ToUpperInvariant());
        var resp = await Api.GetAsync($"api/quotes?symbols={encoded}", ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        return JsonSerializer.Serialize(json, PrettyJson);
    }
}
