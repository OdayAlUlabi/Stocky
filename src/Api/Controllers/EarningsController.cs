using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stocky.Api.Dtos;
using Stocky.Api.Services;

namespace Stocky.Api.Controllers;

/// <summary>
/// SCR-031 Earnings calendar.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public class EarningsController(IMarketDataProvider provider) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<EarningsEventDto>>> Get([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct = default)
    {
        var f = from ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var t = to ?? f.AddDays(14);
        if (t < f) return BadRequest("'to' must be on or after 'from'.");
        var items = await provider.GetEarningsAsync(f, t, ct);
        return Ok(items);
    }
}
