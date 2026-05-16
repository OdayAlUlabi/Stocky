using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;
using Stocky.Api.Services;

namespace Stocky.Api.Controllers;

/// <summary>
/// SCR-009. Returns TWRR (chain-linked daily) and MWRR (XIRR) over the requested
/// window using PortfolioSnapshot rows for value, with external cash flows
/// derived from the transaction ledger (BUY = +contribution, SELL = -withdrawal).
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

        if (snapshots.Count == 0)
        {
            return new PerformanceDto(portfolioId, portfolio.BaseCurrency, 0m, 0m, 0m, 0m,
                Array.Empty<PerformancePointDto>());
        }

        // Per-day external flow from the transaction ledger. BUY puts money in
        // (+cost incl. fee), SELL takes money out (-proceeds net of fee). DIVIDEND
        // and other corporate actions are treated as portfolio return, not flow.
        var firstDate = snapshots[0].Date.ToDateTime(TimeOnly.MinValue);
        var lastDate = snapshots[^1].Date.ToDateTime(TimeOnly.MaxValue);
        var txs = await db.Transactions
            .Where(t => t.PortfolioId == portfolioId
                && t.ExecutedAt >= firstDate && t.ExecutedAt <= lastDate
                && (t.Type == TransactionType.Buy || t.Type == TransactionType.Sell
                    || t.Type == TransactionType.Deposit || t.Type == TransactionType.Withdrawal))
            .ToListAsync();

        var flowByDay = new Dictionary<DateOnly, decimal>();
        foreach (var t in txs)
        {
            var d = DateOnly.FromDateTime(t.ExecutedAt.UtcDateTime);
            var amount = t.Type switch
            {
                TransactionType.Buy => t.Quantity * t.Price + t.Fee,
                TransactionType.Sell => -(t.Quantity * t.Price - t.Fee),
                TransactionType.Deposit => t.Quantity * t.Price,
                TransactionType.Withdrawal => -(t.Quantity * t.Price),
                _ => 0m
            };
            flowByDay[d] = flowByDay.TryGetValue(d, out var existing) ? existing + amount : amount;
        }

        var points = new List<(DateOnly Date, decimal Value, decimal ExternalFlow)>(snapshots.Count);
        var series = new List<PerformancePointDto>(snapshots.Count);
        decimal? baseValue = null;
        decimal best = 0m, worst = 0m;
        decimal? prevValue = null;
        decimal twrProduct = 1m;

        foreach (var s in snapshots)
        {
            baseValue ??= s.MarketValue == 0 ? null : s.MarketValue;
            var flow = flowByDay.TryGetValue(s.Date, out var f) ? f : 0m;
            points.Add((s.Date, s.MarketValue, flow));

            if (prevValue is > 0m)
            {
                var r = (s.MarketValue - prevValue.Value - flow) / prevValue.Value;
                twrProduct *= 1m + r;
                var dayPct = r * 100m;
                if (dayPct > best) best = dayPct;
                if (dayPct < worst) worst = dayPct;
            }
            var twr = Math.Round((twrProduct - 1m) * 100m, 4);
            series.Add(new PerformancePointDto(
                new DateTimeOffset(s.Date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                s.MarketValue, s.CostBasis, twr));
            prevValue = s.MarketValue;
        }

        var totalTwr = Math.Round((twrProduct - 1m) * 100m, 4);

        // MWRR via XIRR: each day's net contribution flows OUT of the investor
        // (negative), and the terminal market value flows IN (positive).
        var cashflows = new List<(DateOnly Date, decimal Amount)>();
        // Treat the opening market value as the very first contribution so the
        // IRR period spans the full window even when there are no inflows in it.
        if (snapshots[0].MarketValue > 0)
        {
            cashflows.Add((snapshots[0].Date, -snapshots[0].MarketValue));
        }
        foreach (var s in snapshots.Skip(1))
        {
            if (flowByDay.TryGetValue(s.Date, out var f) && f != 0m)
                cashflows.Add((s.Date, -f));
        }
        cashflows.Add((snapshots[^1].Date, snapshots[^1].MarketValue));
        var mwr = Math.Round(ReturnsCalculator.Mwrr(cashflows) * 100m, 2);

        return new PerformanceDto(portfolioId, portfolio.BaseCurrency, totalTwr, mwr,
            Math.Round(best, 2), Math.Round(worst, 2), series);
    }
}
