using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;

namespace Stocky.Api.Controllers;

/// <summary>
/// SCR-005 — single-symbol drill-down. Combines current holding, open lots,
/// transaction history for that symbol, and a price history series.
/// </summary>
[ApiController]
[Route("api/portfolios/{portfolioId:guid}/positions/{symbol}")]
public class PositionDetailController(StockyDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PositionDetailDto>> Get(Guid portfolioId, string symbol)
    {
        var ownerId = User.GetOwnerId();
        var portfolio = await db.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId && p.OwnerId == ownerId);
        if (portfolio is null) return NotFound();

        symbol = symbol.ToUpperInvariant();
        var holding = await db.Holdings.FirstOrDefaultAsync(h => h.PortfolioId == portfolioId && h.Symbol == symbol);
        var instrument = await db.Instruments.FirstOrDefaultAsync(i => i.Symbol == symbol);
        var metadata = await db.InstrumentMetadata.FirstOrDefaultAsync(m => m.Symbol == symbol);
        if (holding is null && instrument is null) return NotFound();

        var latestQuote = await db.PriceQuotes.Where(q => q.Symbol == symbol)
            .OrderByDescending(q => q.AsOf).FirstOrDefaultAsync();

        var txs = await db.Transactions
            .Where(t => t.PortfolioId == portfolioId && t.Symbol == symbol)
            .OrderByDescending(t => t.ExecutedAt)
            .ToListAsync();

        var lots = await db.TaxLots
            .Where(l => l.PortfolioId == portfolioId && l.Symbol == symbol && l.RemainingQuantity > 0)
            .OrderBy(l => l.OpenedAt)
            .ToListAsync();

        var realized = await db.RealizedGains
            .Where(g => g.PortfolioId == portfolioId && g.Symbol == symbol)
            .SumAsync(g => (decimal?)g.Gain) ?? 0m;

        var dividends = txs.Where(t => t.Type == TransactionType.Dividend).Sum(t => t.Quantity * t.Price);

        var qty = holding?.Quantity ?? 0m;
        var avgCost = holding?.AverageCost ?? 0m;
        decimal? mv = latestQuote is null ? null : qty * latestQuote.Price;
        var unrealized = mv is null ? 0m : mv.Value - qty * avgCost;
        var unrealizedPct = qty * avgCost == 0 ? 0m : Math.Round(unrealized / (qty * avgCost) * 100m, 4);

        var since = DateTimeOffset.UtcNow.AddDays(-180);
        var rawQuotes = await db.PriceQuotes
            .Where(q => q.Symbol == symbol && q.AsOf >= since)
            .OrderBy(q => q.AsOf)
            .Select(q => new { q.AsOf, q.Price })
            .ToListAsync();
        var history = rawQuotes
            .GroupBy(q => q.AsOf.UtcDateTime.Date)
            .Select(g => new ValuePointDto(new DateTimeOffset(g.Key, TimeSpan.Zero), g.OrderByDescending(x => x.AsOf).First().Price))
            .ToList();

        return new PositionDetailDto(
            symbol,
            instrument?.Name ?? symbol,
            instrument?.AssetClass ?? "Equity",
            metadata?.Sector,
            instrument?.Currency ?? portfolio.BaseCurrency,
            qty, avgCost, latestQuote?.Price, mv, unrealized, unrealizedPct,
            realized, dividends,
            lots.Select(l => new TaxLotDto(l.Id, l.OpenedAt, l.Quantity, l.RemainingQuantity, l.CostPerShare, l.RemainingQuantity * l.CostPerShare)).ToList(),
            txs.Select(t => new TransactionDto(t.Id, t.Symbol, t.Type.ToString(), t.Quantity, t.Price, t.Fee, t.Currency, t.ExecutedAt, t.Notes)).ToList(),
            history,
            latestQuote?.Change,
            latestQuote?.ChangePercent,
            metadata?.Industry,
            metadata?.Country,
            instrument?.Exchange,
            metadata?.MarketCap,
            metadata?.Beta,
            metadata?.DividendYield);
    }
}
