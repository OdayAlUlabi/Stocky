using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stocky.Api.Data;
using Stocky.Api.Dtos;
using Stocky.Api.Services;

namespace Stocky.Api.Controllers;

/// <summary>
/// Portfolio rebalance: per-symbol target weights and the trades needed to
/// restore them. Companion to /allocation.
/// </summary>
[ApiController]
[Authorize]
[Route("api/portfolios/{portfolioId:guid}/rebalance")]
public class RebalanceController(RebalanceService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<RebalanceReportDto>> Get(Guid portfolioId, CancellationToken ct)
    {
        var ownerId = User.GetOwnerId();
        var report = await service.ComputeAsync(portfolioId, ownerId, ct);
        return report is null ? NotFound() : Ok(report);
    }

    [HttpGet("targets")]
    public async Task<ActionResult<IEnumerable<RebalanceTargetDto>>> GetTargets(Guid portfolioId, CancellationToken ct)
    {
        var ownerId = User.GetOwnerId();
        var targets = await service.GetTargetsAsync(portfolioId, ownerId, ct);
        return Ok(targets);
    }

    [HttpPut("targets")]
    public async Task<ActionResult<IEnumerable<RebalanceTargetDto>>> PutTargets(
        Guid portfolioId,
        [FromBody] IReadOnlyList<RebalanceTargetDto> targets,
        CancellationToken ct)
    {
        var ownerId = User.GetOwnerId();
        try
        {
            var saved = await service.SetTargetsAsync(portfolioId, ownerId, targets ?? Array.Empty<RebalanceTargetDto>(), ct);
            return saved is null ? NotFound() : Ok(saved);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
