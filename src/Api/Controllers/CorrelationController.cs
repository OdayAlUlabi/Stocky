using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Dtos;
using Stocky.Api.Services;

namespace Stocky.Api.Controllers;

/// <summary>
/// Pearson correlation matrix of log returns across every symbol currently
/// held in the portfolio. Useful for spotting concentration / lack of
/// diversification (e.g. a portfolio of AAPL+MSFT+GOOG will show high
/// pairwise correlations because they all move with US tech).
/// </summary>
[ApiController]
[Route("api/portfolios/{portfolioId:guid}/correlation")]
public class CorrelationController(StockyDbContext db, IMarketDataProvider market) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CorrelationDto>> Get(
        Guid portfolioId,
        [FromQuery] int days = 90,
        CancellationToken ct = default)
    {
        var ownerId = User.GetOwnerId();
        var portfolio = await db.Portfolios
            .FirstOrDefaultAsync(p => p.Id == portfolioId && p.OwnerId == ownerId, ct);
        if (portfolio is null) return NotFound();
        days = Math.Clamp(days, 14, 1825);

        var symbols = await db.Holdings
            .Where(h => h.PortfolioId == portfolioId && h.Quantity > 0)
            .Select(h => h.Symbol)
            .Distinct()
            .OrderBy(s => s)
            .ToListAsync(ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = today.AddDays(-days);

        if (symbols.Count < 2)
        {
            return new CorrelationDto(portfolioId, from, today, symbols,
                Array.Empty<IReadOnlyList<decimal>>());
        }

        var bars = await market.GetDailyBarsAsync(symbols, from, today, ct);

        // Align all symbols on the intersection of their available dates so
        // returns are pairwise comparable. Take dates that every symbol has.
        var commonDates = bars.Values
            .Where(v => v.Count > 0)
            .Select(v => (IEnumerable<DateOnly>)v.Select(b => b.Date))
            .Aggregate((a, b) => a.Intersect(b))
            .OrderBy(d => d)
            .ToList();

        if (commonDates.Count < 3)
        {
            return new CorrelationDto(portfolioId, from, today, symbols,
                Array.Empty<IReadOnlyList<decimal>>());
        }

        var returnsBySymbol = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var sym in symbols)
        {
            if (!bars.TryGetValue(sym, out var list) || list.Count == 0)
            {
                returnsBySymbol[sym] = Array.Empty<double>();
                continue;
            }
            var byDate = list.ToDictionary(b => b.Date, b => b.Close);
            var aligned = new List<decimal>(commonDates.Count);
            foreach (var d in commonDates)
            {
                if (byDate.TryGetValue(d, out var close)) aligned.Add(close);
            }
            returnsBySymbol[sym] = CorrelationCalculator.LogReturns(aligned);
        }

        var matrix = new List<IReadOnlyList<decimal>>(symbols.Count);
        for (var i = 0; i < symbols.Count; i++)
        {
            var row = new decimal[symbols.Count];
            for (var j = 0; j < symbols.Count; j++)
            {
                if (i == j) { row[j] = 1m; continue; }
                row[j] = CorrelationCalculator.Pearson(
                    returnsBySymbol[symbols[i]], returnsBySymbol[symbols[j]]);
            }
            matrix.Add(row);
        }

        return new CorrelationDto(portfolioId, commonDates[0], commonDates[^1], symbols, matrix);
    }
}
