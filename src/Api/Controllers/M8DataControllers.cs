using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stocky.Api.Dtos;
using Stocky.Api.Services;

namespace Stocky.Api.Controllers;

/// <summary>M8 #5 — Insider trades feed (Form 4 etc).</summary>
[ApiController]
[Authorize]
[Route("api/insider-trades")]
public class InsiderTradesController(IExtendedMarketDataProvider provider) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<InsiderTradeDto>>> Get([FromQuery] string symbol, [FromQuery] int limit = 25, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return BadRequest("symbol required");
        limit = Math.Clamp(limit, 1, 100);
        var items = await provider.GetInsiderTradesAsync(symbol, limit, ct);
        return Ok(items);
    }
}

/// <summary>M8 #6 — Short interest snapshot and history.</summary>
[ApiController]
[Authorize]
[Route("api/short-interest")]
public class ShortInterestController(IExtendedMarketDataProvider provider) : ControllerBase
{
    [HttpGet("{symbol}")]
    public async Task<ActionResult<ShortInterestDto>> Get(string symbol, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return BadRequest("symbol required");
        var data = await provider.GetShortInterestAsync(symbol, ct);
        return Ok(data);
    }
}

/// <summary>M8 #7 — Economic calendar (FRED + Finnhub).</summary>
[ApiController]
[Authorize]
[Route("api/calendar/economic")]
public class EconomicCalendarController(IExtendedMarketDataProvider provider) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<EconomicEventDto>>> Get([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct = default)
    {
        var f = from ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var t = to ?? f.AddDays(14);
        if (t < f) (f, t) = (t, f);
        if (t.DayNumber - f.DayNumber > 92) t = f.AddDays(92);
        var items = await provider.GetEconomicCalendarAsync(f, t, ct);
        return Ok(items);
    }
}

/// <summary>M8 #8 — Options flow / unusual options activity.</summary>
[ApiController]
[Authorize]
[Route("api/options-flow")]
public class OptionsFlowController(IExtendedMarketDataProvider provider) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<OptionsFlowDto>> Get([FromQuery] string symbol, [FromQuery] int limit = 25, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return BadRequest("symbol required");
        limit = Math.Clamp(limit, 1, 100);
        var data = await provider.GetOptionsFlowAsync(symbol, limit, ct);
        return Ok(data);
    }
}
