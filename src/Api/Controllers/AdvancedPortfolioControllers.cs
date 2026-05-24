using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stocky.Api.Data;
using Stocky.Api.Dtos;
using Stocky.Api.Services;

namespace Stocky.Api.Controllers;

/// <summary>
/// Advanced portfolio analytics endpoints — implement GitHub milestone #8
/// items 1.3, 1.4, 2.1, 2.2, 2.4, 2.5, 4.3.
/// </summary>
[ApiController]
[Authorize]
[Route("api/portfolios/{portfolioId:guid}/var-suite")]
public class VarSuiteController(AdvancedRiskService svc) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<VarSuiteDto>> Get(
        Guid portfolioId,
        [FromQuery] decimal confidence = 0.95m,
        [FromQuery] int holdingDays = 1,
        [FromQuery] int simulations = 10000,
        CancellationToken ct = default)
    {
        var dto = await svc.BuildVarAsync(portfolioId, User.GetOwnerId(), confidence, holdingDays, simulations, ct);
        return dto is null ? NotFound() : Ok(dto);
    }
}

[ApiController]
[Authorize]
[Route("api/portfolios/{portfolioId:guid}/stress-test")]
public class StressTestController(AdvancedRiskService svc) : ControllerBase
{
    [HttpGet("scenarios")]
    public ActionResult<IEnumerable<StressScenarioDto>> Scenarios()
        => Ok(AdvancedRiskService.PresetScenarios);

    [HttpPost]
    public async Task<ActionResult<StressTestResultDto>> Run(
        Guid portfolioId,
        [FromBody] StressTestRequest req,
        CancellationToken ct = default)
    {
        if (req is null) return BadRequest();
        req = req with { PortfolioId = portfolioId };
        var dto = await svc.RunStressAsync(req, User.GetOwnerId(), ct);
        return dto is null ? NotFound() : Ok(dto);
    }
}

[ApiController]
[Authorize]
[Route("api/portfolios/{portfolioId:guid}/concentration")]
public class ConcentrationController(AdvancedRiskService svc) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ConcentrationRiskDto>> Get(
        Guid portfolioId,
        [FromQuery] decimal maxPosition = 0.10m,
        [FromQuery] decimal maxSector = 0.30m,
        [FromQuery] decimal maxCountry = 0.40m,
        CancellationToken ct = default)
    {
        var dto = await svc.BuildConcentrationAsync(portfolioId, User.GetOwnerId(), maxPosition, maxSector, maxCountry, ct);
        return dto is null ? NotFound() : Ok(dto);
    }
}

[ApiController]
[Authorize]
[Route("api/portfolios/{portfolioId:guid}/liquidity-risk")]
public class LiquidityRiskController(AdvancedRiskService svc) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<LiquidityRiskDto>> Get(
        Guid portfolioId,
        [FromQuery] decimal participation = 0.20m,
        [FromQuery] int thresholdDays = 5,
        [FromQuery] int advLookback = 30,
        CancellationToken ct = default)
    {
        var dto = await svc.BuildLiquidityAsync(portfolioId, User.GetOwnerId(), participation, thresholdDays, advLookback, ct);
        return dto is null ? NotFound() : Ok(dto);
    }
}

[ApiController]
[Authorize]
[Route("api/momentum")]
public class MomentumController(MomentumScoringService svc) : ControllerBase
{
    [HttpPost("scores")]
    public async Task<ActionResult<MomentumScoreSetDto>> Scores([FromBody] MomentumRequest req, CancellationToken ct = default)
    {
        if (req is null || req.Universe is null || req.Universe.Count == 0) return BadRequest("universe required");
        var dto = await svc.ScoreAsync(req, ct);
        return dto is null ? BadRequest("no data") : Ok(dto);
    }
}

[ApiController]
[Authorize]
[Route("api/optimizer")]
public class OptimizerController(PortfolioOptimizerService svc) : ControllerBase
{
    [HttpPost("run")]
    public async Task<ActionResult<OptimizerResultDto>> Run([FromBody] OptimizerRequest req, CancellationToken ct = default)
    {
        if (req is null || req.Symbols is null || req.Symbols.Count < 2)
            return BadRequest("at least two symbols required");
        var dto = await svc.RunAsync(req, ct);
        return dto is null ? BadRequest("not enough overlapping history to optimize") : Ok(dto);
    }
}

[ApiController]
[Authorize]
[Route("api/position-sizing")]
public class PositionSizingController(PositionSizingService svc) : ControllerBase
{
    [HttpPost]
    public ActionResult<PositionSizingResultDto> Compute([FromBody] PositionSizingRequest req)
    {
        if (req is null) return BadRequest();
        if (req.AccountSize <= 0 || req.EntryPrice <= 0) return BadRequest("accountSize and entryPrice must be positive");
        return Ok(svc.Compute(req));
    }
}
