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
public class DashboardController(StockyDbContext db, PortfolioLedgerService ledger, PortfolioHistoryService history, HoldingsCalculator holdingsCalc) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DashboardDto>> Get([FromQuery] Guid? portfolioId, CancellationToken ct = default)
    {
        var ownerId = User.GetOwnerId();

        var portfolioQuery = db.Portfolios.Where(p => p.OwnerId == ownerId);
        if (portfolioId.HasValue) portfolioQuery = portfolioQuery.Where(p => p.Id == portfolioId.Value);

        var portfolios = await portfolioQuery.ToListAsync(ct);

        var primary = portfolios.FirstOrDefault();
        var currency = primary?.BaseCurrency ?? "USD";
        var name = portfolioId.HasValue
            ? (primary?.Name ?? "Portfolio")
            : "All Portfolios";

        // Derive holdings live from the transaction journal — never trust the
        // materialized Holdings table here.
        var holdings = (await holdingsCalc.ComputeManyAsync(
            portfolios.Select(p => p.Id).ToList(), ct)).ToList();
        decimal cashBalance = 0m;
        foreach (var p in portfolios)
        {
            cashBalance += await ledger.GetCashBalanceAsync(p.Id, ct);
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

        var valueHistory = await BuildValueHistoryAsync(portfolios, ownerId, ct);

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
    /// Build a daily portfolio value series from the transaction ledger via
    /// <see cref="PortfolioHistoryService"/>. Returns per-day Cash, MarketValue
    /// (stock value), and Total so the dashboard chart can stack the two
    /// components and the user can see how each evolved over time.
    /// When multiple portfolios are in scope, the daily values are summed
    /// across portfolios by date.
    /// </summary>
    private async Task<List<ValuePointDto>> BuildValueHistoryAsync(
        List<Portfolio> portfolios, string ownerId, CancellationToken ct)
    {
        if (portfolios.Count == 0) return new List<ValuePointDto>();

        // Aggregate per-day across all portfolios in scope.
        var byDate = new SortedDictionary<DateOnly, (decimal Cash, decimal Mv)>();
        foreach (var p in portfolios)
        {
            var dto = await history.BuildAsync(p.Id, ownerId, ct);
            if (dto is null) continue;
            foreach (var pt in dto.Series)
            {
                if (!byDate.TryGetValue(pt.Date, out var agg))
                {
                    byDate[pt.Date] = (pt.Cash, pt.MarketValue);
                }
                else
                {
                    byDate[pt.Date] = (agg.Cash + pt.Cash, agg.Mv + pt.MarketValue);
                }
            }
        }

        var series = new List<ValuePointDto>(byDate.Count);
        foreach (var (day, agg) in byDate)
        {
            var total = Math.Round(agg.Cash + agg.Mv, 2);
            series.Add(new ValuePointDto(
                new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                total,
                Math.Round(agg.Cash, 2),
                Math.Round(agg.Mv, 2)));
        }
        return series;
    }
}
