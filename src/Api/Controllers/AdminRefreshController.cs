using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stocky.Api.Services;

namespace Stocky.Api.Controllers;

/// <summary>
/// On-demand triggers for the market-data refresh jobs. Same implementation
/// as the periodic <c>QuoteRefresher</c> / <c>HistoricalDataBackfillJob</c>
/// background services — useful for forcing a pull outside market hours or
/// right after credentials are rotated.
/// </summary>
/// <remarks>
/// If <c>Admin:ApiKey</c> is configured, callers must send a matching
/// <c>X-Admin-Key</c> header. When the setting is absent the endpoints are
/// open (intended for local dev / single-user prod).
/// </remarks>
[ApiController]
[Route("admin/refresh")]
[AllowAnonymous]
public sealed class AdminRefreshController(
    DataRefreshService refresher,
    IConfiguration config,
    ILogger<AdminRefreshController> logger) : ControllerBase
{
    private IActionResult? CheckAdminKey()
    {
        var expected = config["Admin:ApiKey"];
        if (string.IsNullOrWhiteSpace(expected)) return null;
        var sent = Request.Headers["X-Admin-Key"].ToString();
        if (!string.Equals(sent, expected, StringComparison.Ordinal))
        {
            logger.LogWarning("Admin refresh: missing/invalid X-Admin-Key");
            return Unauthorized(new { error = "missing or invalid X-Admin-Key" });
        }
        return null;
    }

    /// <summary>Force a one-off intraday quote refresh (writes a PriceQuote row per symbol).</summary>
    [HttpPost("quotes")]
    public async Task<IActionResult> RefreshQuotes(CancellationToken ct)
    {
        if (CheckAdminKey() is { } unauth) return unauth;
        var result = await refresher.RefreshQuotesOnceAsync(ct);
        return Ok(new { ok = true, kind = "quotes", result });
    }

    /// <summary>Force a one-off historical daily-bar backfill from each symbol's earliest transaction date.</summary>
    [HttpPost("history")]
    public async Task<IActionResult> RefreshHistory(CancellationToken ct)
    {
        if (CheckAdminKey() is { } unauth) return unauth;
        var result = await refresher.BackfillHistoricalOnceAsync(ct);
        return Ok(new { ok = true, kind = "history", result });
    }

    /// <summary>Run both refreshers back-to-back.</summary>
    [HttpPost("all")]
    public async Task<IActionResult> RefreshAll(CancellationToken ct)
    {
        if (CheckAdminKey() is { } unauth) return unauth;
        var quotes = await refresher.RefreshQuotesOnceAsync(ct);
        var history = await refresher.BackfillHistoricalOnceAsync(ct);
        return Ok(new { ok = true, quotes, history });
    }
}
