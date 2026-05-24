using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Dtos;

namespace Stocky.Api.Controllers;

/// <summary>
/// SCR-010 — multi-pivot allocation: asset class, sector, currency, symbol.
/// </summary>
[ApiController]
[Route("api/portfolios/{portfolioId:guid}/allocation")]
public class AllocationController(StockyDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AllocationDto>> Get(Guid portfolioId)
    {
        var ownerId = User.GetOwnerId();
        var portfolio = await db.Portfolios.Include(p => p.Holdings)
            .FirstOrDefaultAsync(p => p.Id == portfolioId && p.OwnerId == ownerId);
        if (portfolio is null) return NotFound();

        var symbols = portfolio.Holdings.Select(h => h.Symbol).ToList();
        var latest = await db.PriceQuotes
            .Where(q => symbols.Contains(q.Symbol))
            .GroupBy(q => q.Symbol)
            .Select(g => g.OrderByDescending(x => x.AsOf).First())
            .ToDictionaryAsync(q => q.Symbol, q => q.Price);
        var instruments = await db.Instruments
            .Where(i => symbols.Contains(i.Symbol))
            .ToDictionaryAsync(i => i.Symbol, i => i);
        var metadata = await db.InstrumentMetadata
            .Where(m => symbols.Contains(m.Symbol))
            .ToDictionaryAsync(m => m.Symbol, m => m);

        var byAsset = new Dictionary<string, decimal>();
        var bySector = new Dictionary<string, decimal>();
        var byCurrency = new Dictionary<string, decimal>();
        var bySymbol = new Dictionary<string, decimal>();
        decimal total = 0m;

        foreach (var h in portfolio.Holdings)
        {
            if (!latest.TryGetValue(h.Symbol, out var px)) continue;
            var value = h.Quantity * px;
            total += value;
            var assetClass = instruments.TryGetValue(h.Symbol, out var inst) ? inst.AssetClass : "Other";
            var sector = metadata.TryGetValue(h.Symbol, out var md) ? (md.Sector ?? "Unclassified") : "Unclassified";
            var currency = inst?.Currency ?? portfolio.BaseCurrency;
            byAsset[assetClass] = byAsset.GetValueOrDefault(assetClass) + value;
            bySector[sector] = bySector.GetValueOrDefault(sector) + value;
            byCurrency[currency] = byCurrency.GetValueOrDefault(currency) + value;
            bySymbol[h.Symbol] = bySymbol.GetValueOrDefault(h.Symbol) + value;
        }

        return new AllocationDto(
            Slice(byAsset, total), Slice(bySector, total), Slice(byCurrency, total), Slice(bySymbol, total),
            total, portfolio.BaseCurrency);
    }

    private static List<AllocationSliceDto> Slice(Dictionary<string, decimal> by, decimal total) =>
        total <= 0
            ? new List<AllocationSliceDto>()
            : by.OrderByDescending(kv => kv.Value)
                .Select(kv => new AllocationSliceDto(kv.Key, kv.Value, Math.Round(kv.Value / total * 100m, 2)))
                .ToList();
}
