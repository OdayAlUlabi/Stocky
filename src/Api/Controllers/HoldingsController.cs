using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;
using Stocky.Api.Services;

namespace Stocky.Api.Controllers;

[ApiController]
[Route("api/portfolios/{portfolioId:guid}/holdings")]
public class HoldingsController(StockyDbContext db, HoldingsCalculator calculator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<HoldingDto>>> List(Guid portfolioId, CancellationToken ct = default)
    {
        var ownerId = User.GetOwnerId();
        var portfolio = await db.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId && p.OwnerId == ownerId, ct);
        if (portfolio is null) return NotFound();

        // Derive holdings on the fly from the transaction journal — never
        // trust the materialized Holdings table here.
        var holdings = await calculator.ComputeAsync(portfolioId, ct);
        var symbols = holdings.Select(h => h.Symbol).ToList();
        var latest = symbols.Count == 0
            ? new Dictionary<string, decimal>()
            : await db.PriceQuotes
                .Where(q => symbols.Contains(q.Symbol))
                .GroupBy(q => q.Symbol)
                .Select(g => g.OrderByDescending(x => x.AsOf).First())
                .ToDictionaryAsync(q => q.Symbol, q => q.Price, ct);

        var result = holdings.Select(h =>
        {
            decimal? latestPrice = latest.TryGetValue(h.Symbol, out var p) ? p : null;
            decimal? marketValue = latestPrice.HasValue ? latestPrice.Value * h.Quantity : null;
            return new HoldingDto(h.Id, h.Symbol, h.Quantity, h.AverageCost, latestPrice, marketValue);
        });
        return Ok(result);
    }
}
