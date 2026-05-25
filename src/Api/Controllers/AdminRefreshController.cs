using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stocky.Api.Services;

namespace Stocky.Api.Controllers;

/// <summary>
/// Admin / ops endpoints to force a data refresh outside of the normal
/// background-service schedule. Useful for verifying provider keys, seeding
/// data off-hours, or recovering after an outage.
/// <para>
/// Optionally gated by config <c>Admin:ApiKey</c> (env <c>Admin__ApiKey</c>):
/// when set, callers must send a matching <c>X-Admin-Key</c> header. When
/// unset, the endpoints are open (the app gateway is the only public ingress).
/// </para>
/// </summary>
[ApiController]
[Route("admin/refresh")]
[AllowAnonymous]
public sealed class AdminRefreshController(
    DataRefreshService refresher,
    IConfiguration config,
    ILogger<AdminRefreshController> logger) : ControllerBase
{
    [HttpPost("quotes")]
    public async Task<IActionResult> Quotes(CancellationToken ct)
    {
        if (CheckAdminKey() is { } unauth) return unauth;
        logger.LogInformation("Admin force-refresh: quotes requested");
        var result = await refresher.RefreshQuotesOnceAsync(ct);
        return Ok(new { ok = true, quotes = result });
    }

    [HttpPost("history")]
    public async Task<IActionResult> History(CancellationToken ct)
    {
        if (CheckAdminKey() is { } unauth) return unauth;
        logger.LogInformation("Admin force-refresh: history requested");
        var result = await refresher.BackfillHistoricalOnceAsync(ct);
        return Ok(new { ok = true, history = result });
    }

    /// <summary>
    /// Pulls fresh reference data from the market-data provider for every
    /// instrument that's a placeholder (Exchange=UNKNOWN), never enriched, or
    /// last enriched more than 30 days ago. Pass <c>?force=true</c> to refresh
    /// every instrument regardless of age.
    /// </summary>
    [HttpPost("instruments")]
    public async Task<IActionResult> Instruments(CancellationToken ct, [FromQuery] bool force = false)
    {
        if (CheckAdminKey() is { } unauth) return unauth;
        logger.LogInformation("Admin force-refresh: instruments requested (force={Force})", force);
        var result = await refresher.EnrichInstrumentsOnceAsync(ct, forceAll: force);
        return Ok(new { ok = true, instruments = result });
    }

    [HttpPost("all")]
    public async Task<IActionResult> All(CancellationToken ct)
    {
        if (CheckAdminKey() is { } unauth) return unauth;
        logger.LogInformation("Admin force-refresh: all requested");
        var quotes = await refresher.RefreshQuotesOnceAsync(ct);
        var history = await refresher.BackfillHistoricalOnceAsync(ct);
        var instruments = await refresher.EnrichInstrumentsOnceAsync(ct);
        return Ok(new { ok = true, quotes, history, instruments });
    }

    private IActionResult? CheckAdminKey()
    {
        var expected = config["Admin:ApiKey"];
        if (string.IsNullOrWhiteSpace(expected)) return null;
        if (!Request.Headers.TryGetValue("X-Admin-Key", out var provided) ||
            !string.Equals(provided.ToString(), expected, StringComparison.Ordinal))
        {
            return Unauthorized(new { error = "missing or invalid X-Admin-Key" });
        }
        return null;
    }
}
