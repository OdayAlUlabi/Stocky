using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;
using Stocky.Api.Services;

namespace Stocky.Api.Controllers;

/// <summary>M10 #51 — alert-side insider event feed + cluster summary. Separate from M8 /api/insider-trades.</summary>
[ApiController]
[Authorize]
[Route("api/insider-events")]
public class InsiderEventsController(IInsiderTradeProvider provider) : ControllerBase
{
    [HttpGet("{symbol}")]
    public async Task<ActionResult<IEnumerable<InsiderEventDto>>> Recent(string symbol, [FromQuery] int days = 30)
    {
        days = Math.Clamp(days, 1, 365);
        var trades = await provider.GetRecentTradesAsync(symbol, days, HttpContext.RequestAborted);
        return Ok(trades.Select(ToDto));
    }

    [HttpGet("{symbol}/cluster")]
    public async Task<ActionResult<InsiderClusterDto>> Cluster(string symbol, [FromQuery] int days = 30)
    {
        days = Math.Clamp(days, 1, 365);
        var trades = (await provider.GetRecentTradesAsync(symbol, days, HttpContext.RequestAborted)).ToList();
        var buys = trades.Where(t => string.Equals(t.TransactionType, "Buy", StringComparison.OrdinalIgnoreCase)).ToList();
        var sells = trades.Where(t => string.Equals(t.TransactionType, "Sell", StringComparison.OrdinalIgnoreCase)).ToList();
        var net = buys.Sum(t => t.Shares) - sells.Sum(t => t.Shares);
        var windowStart = trades.Count == 0 ? DateTimeOffset.UtcNow.AddDays(-days) : trades.Min(t => t.FiledAt);
        var windowEnd = trades.Count == 0 ? DateTimeOffset.UtcNow : trades.Max(t => t.FiledAt);
        return Ok(new InsiderClusterDto(
            symbol.ToUpperInvariant(),
            buys.Count,
            sells.Count,
            net,
            windowStart,
            windowEnd,
            trades.Select(ToDto).ToList()));
    }

    private static InsiderEventDto ToDto(InsiderTrade t) =>
        new(t.Id, t.Symbol, t.InsiderName, t.Relation, t.TransactionType, t.Shares, t.Price, t.FiledAt);
}
