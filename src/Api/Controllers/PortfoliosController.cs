using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;

namespace Stocky.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class PortfoliosController(StockyDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PortfolioDto>>> List()
    {
        var ownerId = User.GetOwnerId();
        var items = await db.Portfolios
            .Where(p => p.OwnerId == ownerId)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new PortfolioDto(p.Id, p.Name, p.BaseCurrency, p.CreatedAt))
            .ToListAsync();
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PortfolioDto>> Get(Guid id)
    {
        var ownerId = User.GetOwnerId();
        var p = await db.Portfolios.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId);
        if (p is null) return NotFound();
        return new PortfolioDto(p.Id, p.Name, p.BaseCurrency, p.CreatedAt);
    }

    [HttpPost]
    public async Task<ActionResult<PortfolioDto>> Create(CreatePortfolioRequest request)
    {
        var ownerId = User.GetOwnerId();
        var p = new Portfolio
        {
            OwnerId = ownerId,
            Name = request.Name,
            BaseCurrency = string.IsNullOrWhiteSpace(request.BaseCurrency) ? "USD" : request.BaseCurrency
        };
        db.Portfolios.Add(p);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = p.Id }, new PortfolioDto(p.Id, p.Name, p.BaseCurrency, p.CreatedAt));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PortfolioDto>> Update(Guid id, UpdatePortfolioRequest request)
    {
        var ownerId = User.GetOwnerId();
        var p = await db.Portfolios.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId);
        if (p is null) return NotFound();
        p.Name = request.Name;
        p.BaseCurrency = request.BaseCurrency;
        await db.SaveChangesAsync();
        return new PortfolioDto(p.Id, p.Name, p.BaseCurrency, p.CreatedAt);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var ownerId = User.GetOwnerId();
        var p = await db.Portfolios.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId);
        if (p is null) return NotFound();
        db.Portfolios.Remove(p);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("{id:guid}/performance")]
    public async Task<ActionResult<PortfolioPerformanceDto>> Performance(Guid id)
    {
        var ownerId = User.GetOwnerId();
        var p = await db.Portfolios
            .Include(x => x.Holdings)
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId);
        if (p is null) return NotFound();

        var symbols = p.Holdings.Select(h => h.Symbol).ToList();
        var latest = await db.PriceQuotes
            .Where(q => symbols.Contains(q.Symbol))
            .GroupBy(q => q.Symbol)
            .Select(g => g.OrderByDescending(x => x.AsOf).First())
            .ToListAsync();
        var latestBySymbol = latest.ToDictionary(q => q.Symbol, q => q.Price);

        decimal marketValue = 0m;
        decimal costBasis = 0m;
        foreach (var h in p.Holdings)
        {
            costBasis += h.Quantity * h.AverageCost;
            if (latestBySymbol.TryGetValue(h.Symbol, out var price))
            {
                marketValue += h.Quantity * price;
            }
        }
        var pnl = marketValue - costBasis;
        var pnlPct = costBasis == 0 ? 0 : Math.Round(pnl / costBasis * 100m, 4);

        return new PortfolioPerformanceDto(p.Id, marketValue, costBasis, pnl, pnlPct, p.BaseCurrency, DateTimeOffset.UtcNow);
    }
}
