using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;
using Stocky.Api.Services;

namespace Stocky.Api.Controllers;

/// <summary>
/// Aggregated payload backing SCR-002 (Dashboard).
/// Single call returns KPIs, allocations, top movers, and a value history series.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class DashboardController(StockyDbContext db, PortfolioLedgerService ledger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DashboardDto>> Get([FromQuery] Guid? portfolioId)
    {
        var ownerId = User.GetOwnerId();

        var portfolioQuery = db.Portfolios.Where(p => p.OwnerId == ownerId);
        if (portfolioId.HasValue) portfolioQuery = portfolioQuery.Where(p => p.Id == portfolioId.Value);

        var portfolios = await portfolioQuery
            .Include(p => p.Holdings)
            .ToListAsync();

        var primary = portfolios.FirstOrDefault();
        var currency = primary?.BaseCurrency ?? "USD";
        var name = portfolioId.HasValue
            ? (primary?.Name ?? "Portfolio")
            : "All Portfolios";

        var holdings = portfolios.SelectMany(p => p.Holdings).ToList();
        decimal cashBalance = 0m;
        foreach (var p in portfolios)
        {
            cashBalance += await ledger.GetCashBalanceAsync(p.Id);
        }
        if (holdings.Count == 0)
        {
            return new DashboardDto(
                portfolioId, name, currency,
                0m, 0m, 0m, 0m, 0m,
                Array.Empty<AllocationSliceDto>(),
                Array.Empty<AllocationSliceDto>(),
                Array.Empty<MoverDto>(),
                Array.Empty<MoverDto>(),
                Array.Empty<ValuePointDto>(),
                DateTimeOffset.UtcNow,
                cashBalance,
                cashBalance);
        }

        var symbols = holdings.Select(h => h.Symbol).Distinct().ToList();

        var latestQuotes = await db.PriceQuotes
            .Where(q => symbols.Contains(q.Symbol))
            .GroupBy(q => q.Symbol)
            .Select(g => g.OrderByDescending(x => x.AsOf).First())
            .ToDictionaryAsync(q => q.Symbol, q => q);

        var instruments = await db.Instruments
            .Where(i => symbols.Contains(i.Symbol))
            .ToDictionaryAsync(i => i.Symbol, i => i);

        decimal totalValue = 0m, costBasis = 0m, dayPnL = 0m;
        var perSymbolValue = new Dictionary<string, decimal>();
        var perSymbolDayPct = new Dictionary<string, decimal>();
        var bySector = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var byClass = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        foreach (var h in holdings)
        {
            costBasis += h.Quantity * h.AverageCost;
            if (!latestQuotes.TryGetValue(h.Symbol, out var quote)) continue;

            var value = h.Quantity * quote.Price;
            totalValue += value;
            perSymbolValue[h.Symbol] = perSymbolValue.GetValueOrDefault(h.Symbol) + value;

            if (quote.Change.HasValue) dayPnL += h.Quantity * quote.Change.Value;
            if (quote.ChangePercent.HasValue) perSymbolDayPct[h.Symbol] = quote.ChangePercent.Value;

            var sector = instruments.TryGetValue(h.Symbol, out var inst) ? inst.AssetClass : "Other";
            bySector[sector] = bySector.GetValueOrDefault(sector) + value;

            var cls = instruments.TryGetValue(h.Symbol, out var inst2) ? inst2.AssetClass : "Other";
            byClass[cls] = byClass.GetValueOrDefault(cls) + value;
        }

        var totalReturn = totalValue - costBasis;
        var totalReturnPct = costBasis == 0 ? 0m : Math.Round(totalReturn / costBasis * 100m, 4);
        var prevValue = totalValue - dayPnL;
        var dayPnLPct = prevValue == 0 ? 0m : Math.Round(dayPnL / prevValue * 100m, 4);

        var sectorSlices = ToSlices(bySector, totalValue);
        var classSlices = ToSlices(byClass, totalValue);

        var movers = perSymbolValue
            .Select(kv => new MoverDto(kv.Key, kv.Value, perSymbolDayPct.GetValueOrDefault(kv.Key)))
            .ToList();
        var topGainers = movers.OrderByDescending(m => m.DayChangePercent).Take(5).ToList();
        var topLosers = movers.OrderBy(m => m.DayChangePercent).Take(5).ToList();

        var valueHistory = await BuildValueHistoryAsync(holdings, symbols);

        return new DashboardDto(
            portfolioId, name, currency,
            totalValue, dayPnL, dayPnLPct, totalReturn, totalReturnPct,
            sectorSlices, classSlices, topGainers, topLosers, valueHistory,
            DateTimeOffset.UtcNow,
            cashBalance,
            totalValue + cashBalance);
    }

    private static List<AllocationSliceDto> ToSlices(Dictionary<string, decimal> by, decimal total)
    {
        if (total <= 0) return new List<AllocationSliceDto>();
        return by
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new AllocationSliceDto(kv.Key, kv.Value, Math.Round(kv.Value / total * 100m, 2)))
            .ToList();
    }

    /// <summary>
    /// Build a daily portfolio value series from cached PriceQuotes history (up to ~90 days).
    /// Returns an empty list if no historic quotes exist; the UI then shows the empty state.
    /// </summary>
    private async Task<List<ValuePointDto>> BuildValueHistoryAsync(List<Holding> holdings, List<string> symbols)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-90);
        var rows = await db.PriceQuotes
            .Where(q => symbols.Contains(q.Symbol) && q.AsOf >= since)
            .OrderBy(q => q.AsOf)
            .Select(q => new { q.Symbol, q.AsOf, q.Price })
            .ToListAsync();
        if (rows.Count == 0) return new List<ValuePointDto>();

        // Group quotes by date (UTC), take the last quote per symbol per day.
        var byDate = rows
            .GroupBy(r => r.AsOf.UtcDateTime.Date)
            .OrderBy(g => g.Key)
            .ToList();

        var latestBySymbol = new Dictionary<string, decimal>();
        var qtyBySymbol = holdings
            .GroupBy(h => h.Symbol)
            .ToDictionary(g => g.Key, g => g.Sum(h => h.Quantity));

        var series = new List<ValuePointDto>(byDate.Count);
        foreach (var day in byDate)
        {
            foreach (var grouping in day.GroupBy(r => r.Symbol))
            {
                latestBySymbol[grouping.Key] = grouping.Last().Price;
            }
            decimal value = 0m;
            foreach (var (sym, qty) in qtyBySymbol)
            {
                if (latestBySymbol.TryGetValue(sym, out var px)) value += qty * px;
            }
            series.Add(new ValuePointDto(new DateTimeOffset(day.Key, TimeSpan.Zero), value));
        }
        return series;
    }
}
