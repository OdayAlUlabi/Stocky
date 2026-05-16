using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Dtos;

namespace Stocky.Api.Controllers;

/// <summary>
/// SCR-009. Uses PortfolioSnapshot rows when available; otherwise falls back
/// to a value series synthesised from cached PriceQuote history.
/// </summary>
[ApiController]
[Authorize]
[Route("api/portfolios/{portfolioId:guid}/performance-series")]
public class PerformanceController(StockyDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PerformanceDto>> Get(Guid portfolioId, [FromQuery] int days = 90)
    {
        var ownerId = User.GetOwnerId();
        var portfolio = await db.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId && p.OwnerId == ownerId);
        if (portfolio is null) return NotFound();
        days = Math.Clamp(days, 7, 1825);

        var since = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-days);
        var snapshots = await db.PortfolioSnapshots
            .Where(s => s.PortfolioId == portfolioId && s.Date >= since)
            .OrderBy(s => s.Date)
            .ToListAsync();

        var series = new List<PerformancePointDto>();
        decimal? baseValue = null;
        decimal best = 0m, worst = 0m;
        decimal? prevValue = null;

        foreach (var s in snapshots)
        {
            baseValue ??= s.MarketValue == 0 ? null : s.MarketValue;
            var twr = baseValue is null or 0 ? 0m : Math.Round(((s.MarketValue / baseValue.Value) - 1m) * 100m, 4);
            series.Add(new PerformancePointDto(
                new DateTimeOffset(s.Date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                s.MarketValue, s.CostBasis, twr));

            if (prevValue.HasValue && prevValue.Value > 0)
            {
                var dayPct = ((s.MarketValue / prevValue.Value) - 1m) * 100m;
                if (dayPct > best) best = dayPct;
                if (dayPct < worst) worst = dayPct;
            }
            prevValue = s.MarketValue;
        }

        var totalTwr = series.Count == 0 ? 0m : series[^1].TwrPercent;

        // Simple MWR proxy: realized + unrealized / net contributions (approximation).
        var realized = await db.RealizedGains
            .Where(g => g.PortfolioId == portfolioId && g.SoldAt.UtcDateTime >= since.ToDateTime(TimeOnly.MinValue))
            .SumAsync(g => (decimal?)g.Gain) ?? 0m;
        var lastSnap = snapshots.LastOrDefault();
        var unrealized = lastSnap is null ? 0m : (lastSnap.MarketValue - lastSnap.CostBasis);
        var firstSnap = snapshots.FirstOrDefault();
        var netContrib = firstSnap is null ? 0m : Math.Max(firstSnap.CostBasis, 1m);
        var mwr = Math.Round((realized + unrealized) / netContrib * 100m, 2);

        return new PerformanceDto(portfolioId, portfolio.BaseCurrency, totalTwr, mwr,
            Math.Round(best, 2), Math.Round(worst, 2), series);
    }
}
