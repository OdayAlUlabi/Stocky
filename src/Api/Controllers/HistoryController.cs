using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stocky.Api.Data;
using Stocky.Api.Dtos;
using Stocky.Api.Services;

namespace Stocky.Api.Controllers;

/// <summary>
/// SCR-041. Full equity curve since the first transaction, plus an event
/// timeline (deposits, buys, sells, dividends, fees, splits, spin-offs,
/// withdrawals) so the UI can overlay markers on the chart.
/// </summary>
[ApiController]
[Authorize]
[Route("api/portfolios/{portfolioId:guid}/history")]
public class HistoryController(PortfolioHistoryService history) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PortfolioHistoryDto>> Get(Guid portfolioId, CancellationToken ct)
    {
        var ownerId = User.GetOwnerId();
        var dto = await history.BuildAsync(portfolioId, ownerId, ct);
        if (dto is null) return NotFound();
        return dto;
    }
}
