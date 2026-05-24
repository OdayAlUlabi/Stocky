using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;

namespace Stocky.Api.Controllers;

[ApiController]
[Route("api/portfolios/{portfolioId:guid}/holdings")]
public class HoldingsController(StockyDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<HoldingDto>>> List(Guid portfolioId)
    {
        var ownerId = User.GetOwnerId();
        var portfolio = await db.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId && p.OwnerId == ownerId);
        if (portfolio is null) return NotFound();

        var holdings = await db.Holdings.Where(h => h.PortfolioId == portfolioId).ToListAsync();
        var symbols = holdings.Select(h => h.Symbol).ToList();
        var latest = await db.PriceQuotes
            .Where(q => symbols.Contains(q.Symbol))
            .GroupBy(q => q.Symbol)
            .Select(g => g.OrderByDescending(x => x.AsOf).First())
            .ToDictionaryAsync(q => q.Symbol, q => q.Price);

        var result = holdings.Select(h =>
        {
            decimal? latestPrice = latest.TryGetValue(h.Symbol, out var p) ? p : null;
            decimal? marketValue = latestPrice.HasValue ? latestPrice.Value * h.Quantity : null;
            return new HoldingDto(h.Id, h.Symbol, h.Quantity, h.AverageCost, latestPrice, marketValue);
        });
        return Ok(result);
    }
}
