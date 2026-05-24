using Microsoft.AspNetCore.Mvc;
using Stocky.Api.Dtos;
using Stocky.Api.Services;

namespace Stocky.Api.Controllers;

/// <summary>
/// M8 issues #2, #102 — Level-2 order book and after-hours / pre-market quotes
/// hanging off /api/quotes/{symbol}.
/// </summary>
[ApiController]
[Route("api/quotes")]
public class QuotesMarketDataController(IExtendedMarketDataProvider provider) : ControllerBase
{
    /// <summary>L2 order book ladder. Default depth 10, max 25.</summary>
    [HttpGet("{symbol}/book")]
    public async Task<ActionResult<OrderBookDto>> GetBook(string symbol, [FromQuery] int depth = 10, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return BadRequest("symbol required");
        var book = await provider.GetOrderBookAsync(symbol, depth, ct);
        return Ok(book);
    }

    /// <summary>Extended-hours quote with session marker (PreMarket/Regular/AfterHours/Closed).</summary>
    [HttpGet("{symbol}/extended")]
    public async Task<ActionResult<ExtendedQuoteDto>> GetExtended(string symbol, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return BadRequest("symbol required");
        var q = await provider.GetExtendedQuoteAsync(symbol, ct);
        return Ok(q);
    }
}
