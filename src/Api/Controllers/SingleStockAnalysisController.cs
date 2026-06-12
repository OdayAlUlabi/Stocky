using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Stocky.Api.Dtos;
using Stocky.Api.Services;

namespace Stocky.Api.Controllers;

/// <summary>
/// Initial symbol analysis endpoint for the single-stock trading bot workflow.
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
}
