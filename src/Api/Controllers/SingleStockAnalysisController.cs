using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Stocky.Api.Dtos;
using Stocky.Api.Services;

namespace Stocky.Api.Controllers;

/// <summary>
/// Symbol analysis endpoints — setup snapshot, backtest, walk-forward, and
/// TD Sequential / Volume Profile (DeMark indicators).
/// </summary>
[ApiController]
[Route("api/stocks/{symbol}/analysis")]
public sealed class SingleStockAnalysisController(SingleStockAnalysisService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SingleStockAnalysisDto>> Get(
        string symbol,
        [FromQuery] string timeframe = "1D",
        [FromQuery] int historyYears = 5,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return BadRequest("symbol required");

        var request = new SingleStockAnalysisRequest(
            symbol,
            timeframe,
            new SingleStockStrategyConfigDto(HistoryYears: Math.Clamp(historyYears, 1, 10)));

        return Ok(await service.BuildAsync(request, ct));
    }

    [HttpPost("backtest")]
    public async Task<ActionResult<SingleStockBacktestDto>> Backtest(
        string symbol,
        [FromQuery] string timeframe = "1D",
        [FromBody] SingleStockStrategyConfigDto? strategy = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return BadRequest("symbol required");

        var bot = HttpContext.RequestServices.GetRequiredService<SingleStockTradingBotService>();
        var request = new SingleStockBacktestRequest(symbol, timeframe, strategy);
        return Ok(await bot.RunBacktestAsync(request, ct));
    }

    [HttpPost("walk-forward")]
    public async Task<ActionResult<SingleStockWalkForwardDto>> WalkForward(
        string symbol,
        [FromQuery] string timeframe = "1D",
        [FromBody] SingleStockStrategyConfigDto? strategy = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return BadRequest("symbol required");

        var bot = HttpContext.RequestServices.GetRequiredService<SingleStockTradingBotService>();
        var request = new SingleStockWalkForwardRequest(symbol, timeframe, strategy);
        return Ok(await bot.RunWalkForwardAsync(request, ct));
    }

    [HttpGet("td-sequential")]
    public async Task<ActionResult<TdSequentialResultDto>> TdSequential(
        string symbol,
        [FromQuery] string timeframe = "1D",
        [FromQuery] int displayBars = 100,
        [FromQuery] int vpLookback = 250,
        [FromQuery] int vpRows = 24,
        [FromQuery] double vpValueAreaPct = 70.0,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return BadRequest("symbol required");

        var svc = HttpContext.RequestServices.GetRequiredService<TdSequentialService>();
        return Ok(await svc.ComputeAsync(
            symbol, timeframe,
            Math.Clamp(displayBars, 20, 500),
            Math.Clamp(vpLookback, 20, 2000),
            Math.Clamp(vpRows, 5, 100),
            Math.Clamp(vpValueAreaPct, 50.0, 95.0),
            ct));
    }
}
