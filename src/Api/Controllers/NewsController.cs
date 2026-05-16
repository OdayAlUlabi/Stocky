using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Dtos;
using Stocky.Api.Services;

namespace Stocky.Api.Controllers;

/// <summary>
/// SCR-030 News. Pulls headlines from the configured market data provider
/// (stub by default) and supports optional symbol filtering.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public class NewsController(IMarketDataProvider provider, StockyDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<NewsItemDto>>> Get([FromQuery] string? symbols, [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        var list = symbols is null ? null : symbols
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToUpperInvariant()).ToList();

        // If the user passed no symbols, prefer their holdings + watchlist.
        if (list is null || list.Count == 0)
        {
            var ownerId = User.GetOwnerId();
            list = await db.Holdings
                .Where(h => h.Portfolio.OwnerId == ownerId)
                .Select(h => h.Symbol)
                .Union(db.WatchlistItems.Where(i => i.Watchlist.OwnerId == ownerId).Select(i => i.Symbol))
                .Distinct()
                .Take(20)
                .ToListAsync(ct);
            if (list.Count == 0) list = null!;
        }

        var items = await provider.GetNewsAsync(list, limit, ct);
        return Ok(items);
    }
}
