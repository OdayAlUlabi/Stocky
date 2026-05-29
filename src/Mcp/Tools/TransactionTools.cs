using System.ComponentModel;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Stocky.Mcp.Tools;

/// <summary>
/// MCP tools for transactions: list, add a buy/sell, and import from CSV.
/// </summary>
[McpServerToolType]
public sealed class TransactionTools(IHttpClientFactory http)
{
    private static readonly JsonSerializerOptions PrettyJson =
        new() { WriteIndented = true };

    private HttpClient Api => http.CreateClient("StockyApi");

    [McpServerTool]
    [Description(
        "List all transactions for a portfolio in reverse-chronological order. " +
        "Each row includes symbol, type (Buy/Sell/Deposit/Withdrawal/Dividend), " +
        "quantity, price, fee, and current market value.")]
    public async Task<string> ListTransactions(
        [Description("Portfolio GUID.")] string portfolioId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(portfolioId))
            return "Error: portfolioId is required.";

        var resp = await Api.GetAsync(
            $"api/portfolios/{Uri.EscapeDataString(portfolioId)}/transactions", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            return $"Portfolio {portfolioId} not found.";
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        return JsonSerializer.Serialize(json, PrettyJson);
    }

    [McpServerTool]
    [Description(
        "Record a new transaction (Buy, Sell, Deposit, Withdrawal, or Dividend) in a portfolio. " +
        "Returns the created transaction with its assigned ID.")]
    public async Task<string> AddTransaction(
        [Description("Portfolio GUID.")] string portfolioId,
        [Description("Ticker symbol (e.g. 'AAPL'). Leave empty for cash transactions like Deposit/Withdrawal.")] string? symbol,
        [Description("Transaction type: Buy | Sell | Deposit | Withdrawal | Dividend")] string type,
        [Description("Number of shares / units (positive value).")] decimal quantity,
        [Description("Price per share / unit in base currency.")] decimal price,
        [Description("Brokerage fee / commission.")] decimal fee = 0m,
        [Description("ISO currency code, e.g. 'USD'.")] string currency = "USD",
        [Description("ISO 8601 date-time of execution. Defaults to now (UTC) if not provided.")] string? executedAt = null,
        [Description("Optional free-text notes.")] string? notes = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(portfolioId))
            return "Error: portfolioId is required.";

        var executedAtParsed = string.IsNullOrWhiteSpace(executedAt)
            ? DateTimeOffset.UtcNow
            : DateTimeOffset.Parse(executedAt);

        var body = new
        {
            symbol = string.IsNullOrWhiteSpace(symbol) ? null : symbol.ToUpperInvariant(),
            type,
            quantity,
            price,
            fee,
            currency,
            executedAt = executedAtParsed,
            notes
        };

        var resp = await Api.PostAsJsonAsync(
            $"api/portfolios/{Uri.EscapeDataString(portfolioId)}/transactions", body, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        return JsonSerializer.Serialize(json, PrettyJson);
    }

    [McpServerTool]
    [Description(
        "Import multiple transactions into a portfolio from a CSV string. " +
        "Expected columns: Symbol, Type, Quantity, Price, Fee, Currency, ExecutedAt, Notes. " +
        "Returns a summary with imported count, skipped count, and any row errors.")]
    public async Task<string> ImportTransactionsCsv(
        [Description("Portfolio GUID.")] string portfolioId,
        [Description("Full CSV content including the header row.")] string csv,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(portfolioId))
            return "Error: portfolioId is required.";
        if (string.IsNullOrWhiteSpace(csv))
            return "Error: csv is required.";

        var body = new { csv };
        var resp = await Api.PostAsJsonAsync(
            $"api/portfolios/{Uri.EscapeDataString(portfolioId)}/transactions/import", body, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        return JsonSerializer.Serialize(json, PrettyJson);
    }

    [McpServerTool]
    [Description(
        "Update an existing transaction in a portfolio. " +
        "All fields are replaced — supply the full updated values, not just what changed. " +
        "Returns the updated transaction. Use ListTransactions to get the transaction ID first.")]
    public async Task<string> UpdateTransaction(
        [Description("Portfolio GUID.")] string portfolioId,
        [Description("Transaction GUID to update.")] string transactionId,
        [Description("Ticker symbol (e.g. 'AAPL'). Leave empty for cash transactions.")] string? symbol,
        [Description("Transaction type: Buy | Sell | Deposit | Withdrawal | Dividend")] string type,
        [Description("Number of shares / units (positive value).")] decimal quantity,
        [Description("Price per share / unit in base currency.")] decimal price,
        [Description("Brokerage fee / commission.")] decimal fee = 0m,
        [Description("ISO currency code, e.g. 'USD'.")] string currency = "USD",
        [Description("ISO 8601 date-time of execution.")] string? executedAt = null,
        [Description("Optional free-text notes.")] string? notes = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(portfolioId)) return "Error: portfolioId is required.";
        if (string.IsNullOrWhiteSpace(transactionId)) return "Error: transactionId is required.";

        var executedAtParsed = string.IsNullOrWhiteSpace(executedAt)
            ? (DateTimeOffset?)null
            : DateTimeOffset.Parse(executedAt);

        var body = new
        {
            symbol = string.IsNullOrWhiteSpace(symbol) ? null : symbol.ToUpperInvariant(),
            type,
            quantity,
            price,
            fee,
            currency,
            executedAt = executedAtParsed,
            notes
        };

        var resp = await Api.PutAsJsonAsync(
            $"api/portfolios/{Uri.EscapeDataString(portfolioId)}/transactions/{Uri.EscapeDataString(transactionId)}", body, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            return $"Transaction {transactionId} not found in portfolio {portfolioId}.";
        if (resp.StatusCode == System.Net.HttpStatusCode.BadRequest)
            return $"Bad request: {await resp.Content.ReadAsStringAsync(ct)}";
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        return JsonSerializer.Serialize(json, PrettyJson);
    }

    [McpServerTool]
    [Description(
        "Delete a transaction from a portfolio. Holdings and tax lots are recomputed immediately. " +
        "Use ListTransactions to get the transaction ID first. This action cannot be undone.")]
    public async Task<string> DeleteTransaction(
        [Description("Portfolio GUID.")] string portfolioId,
        [Description("Transaction GUID to delete.")] string transactionId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(portfolioId)) return "Error: portfolioId is required.";
        if (string.IsNullOrWhiteSpace(transactionId)) return "Error: transactionId is required.";

        var resp = await Api.DeleteAsync(
            $"api/portfolios/{Uri.EscapeDataString(portfolioId)}/transactions/{Uri.EscapeDataString(transactionId)}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            return $"Transaction {transactionId} not found in portfolio {portfolioId}.";
        resp.EnsureSuccessStatusCode();
        return $"Transaction {transactionId} deleted successfully.";
    }

    [McpServerTool]
    [Description("Get realized wash-sale adjustments for a portfolio to identify disallowed losses.")]
    public async Task<string> GetWashSales(
        [Description("Portfolio GUID.")] string portfolioId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(portfolioId))
            return "Error: portfolioId is required.";

        var resp = await Api.GetAsync(
            $"api/portfolios/{Uri.EscapeDataString(portfolioId)}/wash-sales", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            return $"Portfolio {portfolioId} not found.";
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        return JsonSerializer.Serialize(json, PrettyJson);
    }
}
