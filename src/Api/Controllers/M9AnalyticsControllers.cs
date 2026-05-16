using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Dtos;
using Stocky.Api.Services;

namespace Stocky.Api.Controllers;

/// <summary>
/// M9 #21. OHLCV daily bars for a single symbol — feeds the TradingView
/// Lightweight Charts component on the Position Detail page.
/// </summary>
[ApiController]
[Authorize]
[Route("api/quotes/{symbol}/bars")]
public class BarsController(IAdvancedMarketDataProvider advanced) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<OhlcBarDto>>> Get(
        string symbol,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int days = 180,
        CancellationToken ct = default)
    {
        days = Math.Clamp(days, 7, 365 * 10);
        var t = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var f = from ?? t.AddDays(-days);
        if (f > t) (f, t) = (t, f);
        var bars = await advanced.GetOhlcAsync(symbol, f, t, ct);
        return Ok(bars);
    }
}

/// <summary>
/// M9 #22. Analyst-rating consensus + price-target distribution for a symbol.
/// </summary>
[ApiController]
[Authorize]
[Route("api/analyst-ratings/{symbol}")]
public class AnalystRatingsController(IAdvancedMarketDataProvider advanced) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AnalystRatingDto>> Get(string symbol, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return BadRequest("symbol required");
        var rating = await advanced.GetAnalystRatingAsync(symbol, ct);
        return Ok(rating);
    }
}

/// <summary>
/// M9 #23. Extended risk-metrics endpoint for a portfolio.
/// </summary>
[ApiController]
[Authorize]
[Route("api/portfolios/{portfolioId:guid}/risk")]
public class RiskController(RiskMetricsService risk) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<RiskMetricsDto>> Get(Guid portfolioId, CancellationToken ct = default)
    {
        var ownerId = User.GetOwnerId();
        var result = await risk.BuildAsync(portfolioId, ownerId, ct);
        return result is null ? NotFound() : Ok(result);
    }
}

/// <summary>
/// M9 #103. Benchmark comparison vs a single ticker or a weighted blend.
/// </summary>
[ApiController]
[Authorize]
[Route("api/portfolios/{portfolioId:guid}/benchmark")]
public class BenchmarkController(BenchmarkComparisonService svc, StockyDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<BenchmarkComparisonDto>> Get(
        Guid portfolioId,
        [FromQuery] string? symbol,
        CancellationToken ct = default)
    {
        var ownerId = User.GetOwnerId();
        var config = string.IsNullOrWhiteSpace(symbol) ? null : new BenchmarkConfigDto(symbol, null);
        var result = await svc.BuildAsync(portfolioId, ownerId, config, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<BenchmarkComparisonDto>> Post(
        Guid portfolioId,
        [FromBody] BenchmarkConfigDto config,
        CancellationToken ct = default)
    {
        var ownerId = User.GetOwnerId();
        var result = await svc.BuildAsync(portfolioId, ownerId, config, ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// Persist the portfolio's default benchmark symbol so subsequent GETs
    /// (and risk metrics / analytics) use it without query overrides.
    /// </summary>
    [HttpPut("symbol")]
    public async Task<IActionResult> PutDefault(Guid portfolioId, [FromBody] BenchmarkConfigDto config, CancellationToken ct = default)
    {
        var ownerId = User.GetOwnerId();
        var portfolio = await db.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId && p.OwnerId == ownerId, ct);
        if (portfolio is null) return NotFound();
        portfolio.BenchmarkSymbol = string.IsNullOrWhiteSpace(config.Symbol) ? null : config.Symbol.ToUpperInvariant();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

/// <summary>
/// M9 #24. Run a hypothetical backtest with monthly contributions and
/// periodic rebalancing to target weights.
/// </summary>
[ApiController]
[Authorize]
[Route("api/portfolios/{portfolioId:guid}/backtest")]
public class BacktestController(BacktestService svc) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<BacktestDto>> Run(
        Guid portfolioId,
        [FromBody] BacktestRequest body,
        CancellationToken ct = default)
    {
        if (body.PortfolioId != portfolioId)
        {
            body = body with { PortfolioId = portfolioId };
        }
        var ownerId = User.GetOwnerId();
        var result = await svc.RunAsync(ownerId, body, ct);
        return result is null ? NotFound() : Ok(result);
    }
}

/// <summary>
/// M9 #104. Goals CRUD + projection.
/// </summary>
[ApiController]
[Authorize]
[Route("api/goals")]
public class GoalsController(GoalsService svc) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<GoalDto>>> List(CancellationToken ct = default)
        => Ok(await svc.ListAsync(User.GetOwnerId(), ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GoalDto>> Get(Guid id, CancellationToken ct = default)
    {
        var result = await svc.GetAsync(User.GetOwnerId(), id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<GoalDto>> Create([FromBody] GoalCreateDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Name required");
        if (dto.TargetValue <= 0) return BadRequest("TargetValue must be positive");
        var result = await svc.CreateAsync(User.GetOwnerId(), dto, ct);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<GoalDto>> Update(Guid id, [FromBody] GoalCreateDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Name required");
        var result = await svc.UpdateAsync(User.GetOwnerId(), id, dto, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        var ok = await svc.DeleteAsync(User.GetOwnerId(), id, ct);
        return ok ? NoContent() : NotFound();
    }
}

/// <summary>
/// M9 #95. Earnings surprise history for a single symbol — used by the
/// per-ticker drilldown on the new earnings calendar page.
/// </summary>
[ApiController]
[Authorize]
[Route("api/earnings/{symbol}/surprises")]
public class EarningsSurpriseController(EarningsSurpriseService svc) : ControllerBase
{
    [HttpGet]
    public ActionResult<IEnumerable<EarningsSurprisePointDto>> Get(string symbol, [FromQuery] int quarters = 8)
        => Ok(svc.Build(symbol, quarters));
}

/// <summary>
/// M9 #95. Scoped earnings calendar — returns events filtered to the user's
/// holdings, a single watchlist, or all upcoming events.
/// </summary>
[ApiController]
[Authorize]
[Route("api/calendar/earnings")]
public class EarningsCalendarController(StockyDbContext db, IMarketDataProvider provider) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<EarningsEventDto>>> Get(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string scope = "holdings",
        [FromQuery] Guid? watchlistId = null,
        CancellationToken ct = default)
    {
        var f = from ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var t = to ?? f.AddDays(30);
        if (t < f) return BadRequest("'to' must be on or after 'from'.");
        if (t.DayNumber - f.DayNumber > 365) t = f.AddDays(365);

        var items = await provider.GetEarningsAsync(f, t, ct);

        HashSet<string>? allowed = null;
        var ownerId = User.GetOwnerId();
        switch (scope?.ToLowerInvariant())
        {
            case "holdings":
                allowed = (await db.Holdings
                    .Where(h => h.Portfolio.OwnerId == ownerId && h.Quantity > 0)
                    .Select(h => h.Symbol)
                    .Distinct()
                    .ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                break;
            case "watchlist":
                var q = db.WatchlistItems.Where(w => w.Watchlist.OwnerId == ownerId);
                if (watchlistId.HasValue) q = q.Where(w => w.WatchlistId == watchlistId.Value);
                allowed = (await q.Select(w => w.Symbol).Distinct().ToListAsync(ct))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                break;
            case "all":
                allowed = null;
                break;
            default:
                return BadRequest("scope must be one of: holdings | watchlist | all");
        }

        var filtered = allowed is null
            ? items
            : items.Where(e => allowed.Contains(e.Symbol)).ToList();
        return Ok(filtered);
    }
}
