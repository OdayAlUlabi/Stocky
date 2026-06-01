using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;
using Stocky.Api.Services;
using System.ComponentModel.DataAnnotations;

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

        // Load strategy sidecar — keyed by symbol.
        var strategyMap = await db.Holdings
            .Where(h => h.PortfolioId == portfolioId)
            .ToDictionaryAsync(h => h.Symbol, h => h.Strategy, ct);

        var result = holdings.Select(h =>
        {
            decimal? latestPrice = latest.TryGetValue(h.Symbol, out var p) ? p : null;
            decimal? marketValue = latestPrice.HasValue ? latestPrice.Value * h.Quantity : null;
            var strategy = strategyMap.TryGetValue(h.Symbol, out var s) ? s.ToString() : "General";
            return new HoldingDto(h.Id, h.Symbol, h.Quantity, h.AverageCost, latestPrice, marketValue, strategy);
        });
        return Ok(result);
    }

    [HttpPatch("{symbol}/strategy")]
    public async Task<IActionResult> SetStrategy(Guid portfolioId, string symbol, [FromBody] SetHoldingStrategyRequest request, CancellationToken ct = default)
    {
        var ownerId = User.GetOwnerId();
        var portfolio = await db.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId && p.OwnerId == ownerId, ct);
        if (portfolio is null) return NotFound();

        if (!Enum.TryParse<PositionStrategy>(request.Strategy, ignoreCase: true, out var strategy))
            return BadRequest(new { error = $"Unknown strategy '{request.Strategy}'. Valid values: {string.Join(", ", Enum.GetNames<PositionStrategy>())}" });

        var holding = await db.Holdings.FirstOrDefaultAsync(h => h.PortfolioId == portfolioId && h.Symbol == symbol, ct);
        if (holding is null)
        {
            holding = new Holding { PortfolioId = portfolioId, Symbol = symbol, Strategy = strategy };
            db.Holdings.Add(holding);
        }
        else
        {
            holding.Strategy = strategy;
        }

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPatch("{symbol}/targets")]
    public async Task<IActionResult> SetTargets(Guid portfolioId, string symbol, [FromBody] SetHoldingTargetsRequest request, CancellationToken ct = default)
    {
        var ownerId = User.GetOwnerId();
        var portfolio = await db.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId && p.OwnerId == ownerId, ct);
        if (portfolio is null) return NotFound();

        var holding = await db.Holdings.FirstOrDefaultAsync(h => h.PortfolioId == portfolioId && h.Symbol == symbol, ct);
        if (holding is null)
        {
            holding = new Holding { PortfolioId = portfolioId, Symbol = symbol };
            db.Holdings.Add(holding);
        }

        holding.Target1 = request.Target1;
        holding.Target2 = request.Target2;
        holding.Target3 = request.Target3;
        holding.StopLoss = request.StopLoss;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
