using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Stocky.Mcp.Tools;

/// <summary>
/// MCP tools for portfolio-level operations: list, dashboard KPIs, performance.
/// </summary>
[McpServerToolType]
public sealed class PortfolioTools(IHttpClientFactory http)
{
    private static readonly JsonSerializerOptions PrettyJson =
        new() { WriteIndented = true };

    private HttpClient Api => http.CreateClient("StockyApi");

    // ── Portfolios ─────────────────────────────────────────────────────────────

    [McpServerTool]
    [Description("List all portfolios owned by the configured user, including ID, name, base currency, and current cash balance.")]
    public async Task<string> ListPortfolios(CancellationToken ct = default)
    {
        var resp = await Api.GetAsync("api/portfolios", ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        return JsonSerializer.Serialize(json, PrettyJson);
    }

    // ── Dashboard KPIs ─────────────────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "Return a full dashboard snapshot for a portfolio: total market value, day P&L, total return, " +
        "cash balance, sector/asset-class allocation slices, top gainers, top losers, and a historical " +
        "value series. Pass the portfolio GUID from list_portfolios, or omit it to aggregate all portfolios.")]
    public async Task<string> GetDashboard(
        [Description("Portfolio GUID (from list_portfolios). Leave empty to aggregate all portfolios.")] string? portfolioId = null,
        CancellationToken ct = default)
    {
        var url = string.IsNullOrWhiteSpace(portfolioId)
            ? "api/dashboard"
            : $"api/dashboard?portfolioId={Uri.EscapeDataString(portfolioId)}";

        var resp = await Api.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        return JsonSerializer.Serialize(json, PrettyJson);
    }

    // ── Performance / Analytics ────────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "Return portfolio performance analytics: TWRR, MWRR/XIRR, max drawdown, Sharpe ratio, " +
        "annualized return, dividend yield, and benchmark comparison where available. " +
        "Requires a specific portfolio GUID.")]
    public async Task<string> GetPerformanceAnalytics(
        [Description("Portfolio GUID (required).")] string portfolioId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(portfolioId))
            return "Error: portfolioId is required.";

        var resp = await Api.GetAsync($"api/portfolios/{Uri.EscapeDataString(portfolioId)}/analytics", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            return $"Portfolio {portfolioId} not found.";
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        return JsonSerializer.Serialize(json, PrettyJson);
    }

    // ── Capital Gains ──────────────────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "Return realized capital gains (short-term and long-term) for a portfolio, " +
        "calculated using the configured cost-basis method (FIFO, LIFO, SpecID, etc.).")]
    public async Task<string> GetCapitalGains(
        [Description("Portfolio GUID.")] string portfolioId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(portfolioId))
            return "Error: portfolioId is required.";

        var resp = await Api.GetAsync($"api/portfolios/{Uri.EscapeDataString(portfolioId)}/capital-gains", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            return $"Portfolio {portfolioId} not found.";
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        return JsonSerializer.Serialize(json, PrettyJson);
    }
}
