using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;

namespace Stocky.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class WatchlistsController(StockyDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<WatchlistDto>>> List()
    {
        var ownerId = User.GetOwnerId();
        var lists = await db.Watchlists
            .Where(w => w.OwnerId == ownerId)
            .Include(w => w.Items)
            .ToListAsync();

        var symbols = lists.SelectMany(l => l.Items.Select(i => i.Symbol)).Distinct().ToList();
        var latest = await db.PriceQuotes
            .Where(q => symbols.Contains(q.Symbol))
            .GroupBy(q => q.Symbol)
            .Select(g => g.OrderByDescending(x => x.AsOf).First())
            .ToDictionaryAsync(q => q.Symbol, q => new { q.Price, q.ChangePercent });

        var result = lists.Select(w => new WatchlistDto(
            w.Id,
            w.Name,
            w.Items.Select(i =>
            {
                latest.TryGetValue(i.Symbol, out var quote);
                return new WatchlistItemDto(i.Id, i.Symbol, quote?.Price, quote?.ChangePercent);
            }).ToList()));

        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<WatchlistDto>> Create(CreateWatchlistRequest request)
    {
        var ownerId = User.GetOwnerId();
        var w = new Watchlist { OwnerId = ownerId, Name = request.Name };
        db.Watchlists.Add(w);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(List), null, new WatchlistDto(w.Id, w.Name, Array.Empty<WatchlistItemDto>()));
    }

    [HttpPost("{id:guid}/items")]
    public async Task<ActionResult<WatchlistItemDto>> AddItem(Guid id, AddWatchlistItemRequest request)
    {
        var ownerId = User.GetOwnerId();
        var w = await db.Watchlists.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId);
        if (w is null) return NotFound();
        if (w.Items.Any(i => i.Symbol == request.Symbol))
            return Conflict("Symbol already in watchlist.");

        if (!await db.Instruments.AnyAsync(i => i.Symbol == request.Symbol))
        {
            db.Instruments.Add(new Instrument { Symbol = request.Symbol, Name = request.Symbol, Exchange = "UNKNOWN" });
        }

        var item = new WatchlistItem { WatchlistId = w.Id, Symbol = request.Symbol };
        db.WatchlistItems.Add(item);
        await db.SaveChangesAsync();
        return new WatchlistItemDto(item.Id, item.Symbol, null, null);
    }

    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    public async Task<IActionResult> RemoveItem(Guid id, Guid itemId)
    {
        var ownerId = User.GetOwnerId();
        var w = await db.Watchlists.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId);
        if (w is null) return NotFound();
        var item = w.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is null) return NotFound();
        db.WatchlistItems.Remove(item);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var ownerId = User.GetOwnerId();
        var w = await db.Watchlists.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId);
        if (w is null) return NotFound();
        db.Watchlists.Remove(w);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
