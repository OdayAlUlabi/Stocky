using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;

namespace Stocky.Api.Controllers;

/// <summary>
/// SCR-008 Reports — high-level money flows over a date window.
/// </summary>
[ApiController]
[Route("api/portfolios/{portfolioId:guid}/reports")]
public class ReportsController(StockyDbContext db) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<ReportSummaryDto>> Summary(Guid portfolioId, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to)
    {
        var ownerId = User.GetOwnerId();
        var portfolio = await db.Portfolios.Include(p => p.Holdings)
            .FirstOrDefaultAsync(p => p.Id == portfolioId && p.OwnerId == ownerId);
        if (portfolio is null) return NotFound();

        var fromDt = from ?? DateTimeOffset.UtcNow.AddYears(-1);
        var toDt = to ?? DateTimeOffset.UtcNow;

        var txs = await db.Transactions
            .Where(t => t.PortfolioId == portfolioId && t.ExecutedAt >= fromDt && t.ExecutedAt <= toDt)
            .ToListAsync();

        var deposits = txs.Where(t => t.Type == TransactionType.Deposit).Sum(t => t.Quantity * t.Price);
        var withdrawals = txs.Where(t => t.Type == TransactionType.Withdrawal).Sum(t => t.Quantity * t.Price);
        var dividends = txs.Where(t => t.Type == TransactionType.Dividend).Sum(t => t.Quantity * t.Price);
        var fees = txs.Sum(t => t.Fee);

        var realized = await db.RealizedGains
            .Where(g => g.PortfolioId == portfolioId && g.SoldAt >= fromDt && g.SoldAt <= toDt)
            .SumAsync(g => (decimal?)g.Gain) ?? 0m;

        var symbols = portfolio.Holdings.Select(h => h.Symbol).ToList();
        var latest = await db.PriceQuotes
            .Where(q => symbols.Contains(q.Symbol))
            .GroupBy(q => q.Symbol)
            .Select(g => g.OrderByDescending(x => x.AsOf).First())
            .ToDictionaryAsync(q => q.Symbol, q => q.Price);

        decimal marketValue = 0m, cost = 0m;
        foreach (var h in portfolio.Holdings)
        {
            cost += h.Quantity * h.AverageCost;
            if (latest.TryGetValue(h.Symbol, out var px)) marketValue += h.Quantity * px;
        }
        var unrealized = marketValue - cost;

        return new ReportSummaryDto(
            deposits, withdrawals, deposits - withdrawals,
            marketValue, realized, unrealized, dividends, fees,
            fromDt, toDt, portfolio.BaseCurrency);
    }

    [HttpGet("dividends")]
    public async Task<ActionResult<IEnumerable<DividendRowDto>>> Dividends(Guid portfolioId, [FromQuery] int? year)
    {
        var ownerId = User.GetOwnerId();
        var portfolio = await db.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId && p.OwnerId == ownerId);
        if (portfolio is null) return NotFound();
        var query = db.Transactions
            .Where(t => t.PortfolioId == portfolioId && t.Type == TransactionType.Dividend);
        if (year.HasValue)
        {
            var start = new DateTimeOffset(new DateTime(year.Value, 1, 1), TimeSpan.Zero);
            var end = start.AddYears(1);
            query = query.Where(t => t.ExecutedAt >= start && t.ExecutedAt < end);
        }
        var rows = await query
            .OrderByDescending(t => t.ExecutedAt)
            .Select(t => new DividendRowDto(t.Symbol ?? "CASH", t.ExecutedAt, t.Quantity * t.Price, t.Currency))
            .ToListAsync();
        return Ok(rows);
    }
}
