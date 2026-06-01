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
        "Returns symbol, quantity, average cost basis, latest market price, total market value, " +
        "and the assigned PositionStrategy (General, LongTerm, Hodl, or MomentumPlays) for each position.")]
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

    [McpServerTool]
    [Description(
        "Get all holdings across every portfolio owned by the caller, grouped by PositionStrategy. " +
        "Strategies are: General (default), LongTerm (buy-and-hold), Hodl (crypto-style hold), MomentumPlays (short-term momentum trades). " +
        "Each holding also includes optional position targets (Target1/TP1, Target2/TP2, Target3/TP3) and a StopLoss price, if set. " +
        "Useful for understanding how positions are classified and what price levels are being tracked across the entire account.")]
    public async Task<string> GetHoldingsByStrategy(CancellationToken ct = default)
    {
        var resp = await Api.GetAsync("api/holdings/by-strategy", ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        return JsonSerializer.Serialize(json, PrettyJson);
    }

    [McpServerTool]
    [Description(
        "Set the PositionStrategy for a holding in a portfolio. " +
        "Valid strategies: General, LongTerm, Hodl, MomentumPlays.")]
    public async Task<string> SetHoldingStrategy(
        [Description("Portfolio GUID.")] string portfolioId,
        [Description("Ticker symbol, e.g. 'AAPL'.")] string symbol,
        [Description("Strategy to assign: General, LongTerm, Hodl, or MomentumPlays.")] string strategy,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(portfolioId) || string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(strategy))
            return "Error: portfolioId, symbol, and strategy are all required.";

        var resp = await Api.PatchAsJsonAsync(
            $"api/portfolios/{Uri.EscapeDataString(portfolioId)}/holdings/{Uri.EscapeDataString(symbol.ToUpperInvariant())}/strategy",
            new { strategy },
            ct);

        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            return $"Portfolio {portfolioId} not found.";
        if (!resp.IsSuccessStatusCode)
            return $"Error: {resp.StatusCode} — {await resp.Content.ReadAsStringAsync(ct)}";
        return $"Strategy for {symbol.ToUpperInvariant()} set to {strategy}.";
    }

    [McpServerTool]
    [Description(
        "Set position targets (take-profit levels) and/or a stop-loss price for a holding. " +
        "Up to three take-profit targets can be set (Target1/TP1, Target2/TP2, Target3/TP3). " +
        "Pass null or omit a value to clear it. All prices should be in the portfolio's base currency.")]
    public async Task<string> SetHoldingTargets(
        [Description("Portfolio GUID.")] string portfolioId,
        [Description("Ticker symbol, e.g. 'AAPL'.")] string symbol,
        [Description("First take-profit target price. Pass null to clear.")] decimal? target1 = null,
        [Description("Second take-profit target price. Pass null to clear.")] decimal? target2 = null,
        [Description("Third take-profit target price. Pass null to clear.")] decimal? target3 = null,
        [Description("Stop-loss price. Pass null to clear.")] decimal? stopLoss = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(portfolioId) || string.IsNullOrWhiteSpace(symbol))
            return "Error: portfolioId and symbol are both required.";

        var resp = await Api.PatchAsJsonAsync(
            $"api/portfolios/{Uri.EscapeDataString(portfolioId)}/holdings/{Uri.EscapeDataString(symbol.ToUpperInvariant())}/targets",
            new { target1, target2, target3, stopLoss },
            ct);

        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            return $"Portfolio {portfolioId} not found.";
        if (!resp.IsSuccessStatusCode)
            return $"Error: {resp.StatusCode} — {await resp.Content.ReadAsStringAsync(ct)}";

        var parts = new List<string>();
        if (target1.HasValue) parts.Add($"TP1={target1}");
        if (target2.HasValue) parts.Add($"TP2={target2}");
        if (target3.HasValue) parts.Add($"TP3={target3}");
        if (stopLoss.HasValue) parts.Add($"StopLoss={stopLoss}");
        var summary = parts.Count > 0 ? string.Join(", ", parts) : "all cleared";
        return $"Targets for {symbol.ToUpperInvariant()} updated: {summary}.";
    }
}
