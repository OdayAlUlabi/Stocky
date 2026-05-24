using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Dtos;
using Stocky.Api.Services;

namespace Stocky.Api.Controllers;

/// <summary>M8 #4 — SEC EDGAR filings feed.</summary>
[ApiController]
[Route("api/filings")]
public class FilingsController(IExtendedMarketDataProvider provider, StockyDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<FilingDto>>> Get([FromQuery] string? symbols, [FromQuery] int limit = 25, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        var list = symbols is null ? null : symbols
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToUpperInvariant()).ToList();
        if (list is null || list.Count == 0)
        {
            var ownerId = User.GetOwnerId();
            list = await db.Holdings
                .Where(h => h.Portfolio.OwnerId == ownerId)
                .Select(h => h.Symbol).Distinct().Take(20).ToListAsync(ct);
        }
        if (list.Count == 0) return Ok(Array.Empty<FilingDto>());
        var items = await provider.GetFilingsAsync(list, limit, ct);
        return Ok(items);
    }
}
