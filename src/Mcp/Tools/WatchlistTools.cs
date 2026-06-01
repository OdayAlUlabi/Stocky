using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Stocky.Mcp.Tools;

/// <summary>
/// MCP tools for watchlist CRUD operations.
/// </summary>
[McpServerToolType]
public sealed class WatchlistTools(IHttpClientFactory http)
{
    private static readonly JsonSerializerOptions PrettyJson =
        new() { WriteIndented = true };

    private HttpClient Api => http.CreateClient("StockyApi");

    // ── List ───────────────────────────────────────────────────────────────────

    [McpServerTool]
    [Description("List all watchlists for the configured user, including each watchlist's ID, name, and items with latest price and daily change percent.")]
    public async Task<string> ListWatchlists(CancellationToken ct = default)
    {
        var resp = await Api.GetAsync("api/watchlists", ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        return JsonSerializer.Serialize(json, PrettyJson);
    }

    // ── Create ─────────────────────────────────────────────────────────────────

    [McpServerTool]
    [Description("Create a new empty watchlist with the given name. Returns the new watchlist's ID and name.")]
    public async Task<string> CreateWatchlist(
        [Description("Display name for the new watchlist.")] string name,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Error: name is required.";

        var resp = await Api.PostAsJsonAsync("api/watchlists", new { name }, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        return JsonSerializer.Serialize(json, PrettyJson);
    }

    // ── Add item ───────────────────────────────────────────────────────────────

    [McpServerTool]
    [Description("Add a ticker symbol to an existing watchlist. Returns the new watchlist item. Use list_watchlists to obtain the watchlist ID.")]
    public async Task<string> AddWatchlistItem(
        [Description("Watchlist GUID (from list_watchlists).")] string watchlistId,
        [Description("Ticker symbol to add, e.g. AAPL.")] string symbol,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(watchlistId))
            return "Error: watchlistId is required.";
        if (string.IsNullOrWhiteSpace(symbol))
            return "Error: symbol is required.";

        var url = $"api/watchlists/{Uri.EscapeDataString(watchlistId)}/items";
        var resp = await Api.PostAsJsonAsync(url, new { symbol = symbol.Trim().ToUpperInvariant() }, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.Conflict)
            return $"Symbol {symbol.ToUpperInvariant()} is already in the watchlist.";
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            return $"Watchlist {watchlistId} not found.";
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        return JsonSerializer.Serialize(json, PrettyJson);
    }

    // ── Remove item ────────────────────────────────────────────────────────────

    [McpServerTool]
    [Description("Remove a ticker symbol from a watchlist by looking up the item by ticker. Use list_watchlists to obtain the watchlist ID.")]
    public async Task<string> RemoveWatchlistItem(
        [Description("Watchlist GUID (from list_watchlists).")] string watchlistId,
        [Description("Ticker symbol to remove, e.g. AAPL.")] string ticker,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(watchlistId))
            return "Error: watchlistId is required.";
        if (string.IsNullOrWhiteSpace(ticker))
            return "Error: ticker is required.";

        // Resolve item ID from ticker by fetching the watchlist
        var listResp = await Api.GetAsync("api/watchlists", ct);
        listResp.EnsureSuccessStatusCode();
        var watchlists = await listResp.Content.ReadFromJsonAsync<JsonElement>(ct);

        Guid? itemId = null;
        foreach (var wl in watchlists.EnumerateArray())
        {
            if (wl.GetProperty("id").GetString() != watchlistId) continue;
            foreach (var item in wl.GetProperty("items").EnumerateArray())
            {
                if (string.Equals(item.GetProperty("symbol").GetString(), ticker.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    itemId = Guid.Parse(item.GetProperty("id").GetString()!);
                    break;
                }
            }
        }

        if (itemId is null)
            return $"Symbol {ticker.Trim().ToUpperInvariant()} not found in watchlist {watchlistId}.";

        var url = $"api/watchlists/{Uri.EscapeDataString(watchlistId)}/items/{itemId}";
        var resp = await Api.DeleteAsync(url, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            return $"Watchlist {watchlistId} or item not found.";
        resp.EnsureSuccessStatusCode();
        return $"Removed {ticker.Trim().ToUpperInvariant()} from watchlist {watchlistId}.";
    }

    // ── Delete watchlist ───────────────────────────────────────────────────────

    [McpServerTool]
    [Description("Delete an entire watchlist and all its items. Use list_watchlists to obtain the watchlist ID.")]
    public async Task<string> DeleteWatchlist(
        [Description("Watchlist GUID to delete (from list_watchlists).")] string watchlistId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(watchlistId))
            return "Error: watchlistId is required.";

        var resp = await Api.DeleteAsync($"api/watchlists/{Uri.EscapeDataString(watchlistId)}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            return $"Watchlist {watchlistId} not found.";
        resp.EnsureSuccessStatusCode();
        return $"Deleted watchlist {watchlistId}.";
    }
}
