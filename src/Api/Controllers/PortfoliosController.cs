using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;
using Stocky.Api.Services;

namespace Stocky.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PortfoliosController(StockyDbContext db, PortfolioLedgerService ledger) : ControllerBase
{
    private static bool TryParseMethod(string? value, out CostBasisMethod method)
    {
        method = CostBasisMethod.Fifo;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return Enum.TryParse(value, ignoreCase: true, out method);
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PortfolioDto>>> List()
    {
        var ownerId = User.GetOwnerId();
        var portfolios = await db.Portfolios
            .Where(p => p.OwnerId == ownerId)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync();
        var items = new List<PortfolioDto>(portfolios.Count);
        foreach (var p in portfolios)
        {
            var cash = await ledger.GetCashBalanceAsync(p.Id);
            items.Add(new PortfolioDto(p.Id, p.Name, p.BaseCurrency, p.CreatedAt, cash, p.CostBasisMethod.ToString()));
        }
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PortfolioDto>> Get(Guid id)
    {
        var ownerId = User.GetOwnerId();
        var p = await db.Portfolios.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId);
        if (p is null) return NotFound();
        var cash = await ledger.GetCashBalanceAsync(p.Id);
        return new PortfolioDto(p.Id, p.Name, p.BaseCurrency, p.CreatedAt, cash, p.CostBasisMethod.ToString());
    }

    [HttpPost]
    public async Task<ActionResult<PortfolioDto>> Create(CreatePortfolioRequest request)
    {
        var ownerId = User.GetOwnerId();
        var p = new Portfolio
        {
            OwnerId = ownerId,
            Name = request.Name.Trim(),
            BaseCurrency = string.IsNullOrWhiteSpace(request.BaseCurrency) ? "USD" : request.BaseCurrency.Trim().ToUpperInvariant(),
            CostBasisMethod = TryParseMethod(request.CostBasisMethod, out var m) ? m : CostBasisMethod.Fifo,
        };
        db.Portfolios.Add(p);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = p.Id },
            new PortfolioDto(p.Id, p.Name, p.BaseCurrency, p.CreatedAt, 0m, p.CostBasisMethod.ToString()));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PortfolioDto>> Update(Guid id, UpdatePortfolioRequest request, [FromServices] TaxLotService taxLots)
    {
        var ownerId = User.GetOwnerId();
        var p = await db.Portfolios.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId);
        if (p is null) return NotFound();
        p.Name = request.Name.Trim();
        p.BaseCurrency = request.BaseCurrency.Trim().ToUpperInvariant();
        var methodChanged = false;
        if (TryParseMethod(request.CostBasisMethod, out var m) && m != p.CostBasisMethod)
        {
            p.CostBasisMethod = m;
            methodChanged = true;
        }
        await db.SaveChangesAsync();
        if (methodChanged)
        {
            await taxLots.RecomputeAsync(p.Id);
        }
        var cash = await ledger.GetCashBalanceAsync(p.Id);
        return new PortfolioDto(p.Id, p.Name, p.BaseCurrency, p.CreatedAt, cash, p.CostBasisMethod.ToString());
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
        var cash = await ledger.GetCashBalanceAsync(p.Id);
        var totalEquity = marketValue + cash;

        return new PortfolioPerformanceDto(p.Id, marketValue, costBasis, pnl, pnlPct, p.BaseCurrency, DateTimeOffset.UtcNow, cash, totalEquity);
    }
}
