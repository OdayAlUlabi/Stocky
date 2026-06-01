using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;
using Stocky.Api.Services;

namespace Stocky.Api.Controllers;

/// <summary>
/// Cross-portfolio holdings grouped by PositionStrategy.
/// </summary>
[ApiController]
[Route("api/holdings")]
public class StrategyController(StockyDbContext db, HoldingsCalculator calculator) : ControllerBase
{
    /// <summary>
    /// Returns all holdings across all portfolios owned by the caller,
    /// annotated with their PositionStrategy.
    /// </summary>
    [HttpGet("by-strategy")]
    public async Task<ActionResult<IEnumerable<StrategyHoldingDto>>> ByStrategy(CancellationToken ct = default)
    {
        var ownerId = User.GetOwnerId();

        var portfolios = await db.Portfolios
            .Where(p => p.OwnerId == ownerId)
            .ToListAsync(ct);

        if (portfolios.Count == 0)
            return Ok(Array.Empty<StrategyHoldingDto>());

        // Load strategy sidecar for all owned portfolios up front.
        var portfolioIds = portfolios.Select(p => p.Id).ToList();
        var strategyMap = await db.Holdings
            .Where(h => portfolioIds.Contains(h.PortfolioId))
            .ToDictionaryAsync(h => (h.PortfolioId, h.Symbol), h => h, ct);

        // Collect all symbols to fetch latest prices in one query.
        var allHoldings = new List<(Guid PortfolioId, string PortfolioName, Holding Computed)>();
        foreach (var portfolio in portfolios)
        {
            var computed = await calculator.ComputeAsync(portfolio.Id, ct);
            foreach (var h in computed)
                allHoldings.Add((portfolio.Id, portfolio.Name, h));
        }

        var symbols = allHoldings.Select(x => x.Computed.Symbol).Distinct().ToList();
        var latest = symbols.Count == 0
            ? new Dictionary<string, decimal>()
            : await db.PriceQuotes
                .Where(q => symbols.Contains(q.Symbol))
                .GroupBy(q => q.Symbol)
                .Select(g => g.OrderByDescending(x => x.AsOf).First())
                .ToDictionaryAsync(q => q.Symbol, q => q.Price, ct);

        var result = allHoldings.Select(x =>
        {
            var sidecar = strategyMap.TryGetValue((x.PortfolioId, x.Computed.Symbol), out var sc) ? sc : null;
            var strategy = sidecar?.Strategy ?? PositionStrategy.General;
            decimal? latestPrice = latest.TryGetValue(x.Computed.Symbol, out var p) ? p : null;
            decimal? marketValue = latestPrice.HasValue ? latestPrice.Value * x.Computed.Quantity : null;
            return new StrategyHoldingDto(
                strategy.ToString(),
                x.Computed.Symbol,
                x.PortfolioId,
                x.PortfolioName,
                x.Computed.Quantity,
                x.Computed.AverageCost,
                latestPrice,
                marketValue,
                sidecar?.Target1,
                sidecar?.Target2,
                sidecar?.Target3,
                sidecar?.StopLoss);
        }).OrderBy(x => x.Strategy).ThenBy(x => x.Symbol);

        return Ok(result);
    }
}
