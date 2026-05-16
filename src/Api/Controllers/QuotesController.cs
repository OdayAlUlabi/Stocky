using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Dtos;

namespace Stocky.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class QuotesController(StockyDbContext db) : ControllerBase
{
    /// <summary>
    /// Returns the latest cached quote per symbol. Symbols are passed as comma-separated query, e.g. ?symbols=MSFT,AAPL
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<QuoteDto>>> Get([FromQuery] string symbols)
    {
        if (string.IsNullOrWhiteSpace(symbols)) return Ok(Array.Empty<QuoteDto>());
        var list = symbols.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToUpperInvariant()).Distinct().ToList();

        var quotes = await db.PriceQuotes
            .Where(q => list.Contains(q.Symbol))
            .GroupBy(q => q.Symbol)
            .Select(g => g.OrderByDescending(x => x.AsOf).First())
            .Select(q => new QuoteDto(q.Symbol, q.Price, q.Change, q.ChangePercent, q.AsOf))
            .ToListAsync();

        return Ok(quotes);
    }
}
