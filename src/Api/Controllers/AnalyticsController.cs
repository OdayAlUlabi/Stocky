using Microsoft.AspNetCore.Mvc;
using Stocky.Api.Data;
using Stocky.Api.Dtos;
using Stocky.Api.Services;

namespace Stocky.Api.Controllers;

/// <summary>
/// Portfolio performance analytics: TWRR, MWRR/XIRR, drawdown, Sharpe, dividend yield.
/// Built on top of <see cref="PortfolioHistoryService"/> so figures stay consistent with the equity curve.
/// </summary>
[ApiController]
[Route("api/portfolios/{portfolioId:guid}/analytics")]
public class AnalyticsController(PortfolioAnalyticsService analytics) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PortfolioAnalyticsDto>> Get(Guid portfolioId, CancellationToken ct)
    {
        var dto = await analytics.BuildAsync(portfolioId, User.GetOwnerId(), ct);
        return dto is null ? NotFound() : dto;
    }
}
