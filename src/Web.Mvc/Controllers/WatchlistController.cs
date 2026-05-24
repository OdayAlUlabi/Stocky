using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stocky.Api.Dtos;
using Stocky.Web.Mvc.Internal;
using StockyApi = Stocky.Api.Controllers;

namespace Stocky.Web.Mvc.Controllers;

[Authorize]
public class WatchlistController : Controller
{
    // GET /Watchlist
    public async Task<IActionResult> Index()
    {
        var list = await this.InvokeAsync<StockyApi.WatchlistsController, IEnumerable<WatchlistDto>>(
            c => c.List()) ?? Array.Empty<WatchlistDto>();
        return View(list.ToList());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return RedirectToAction(nameof(Index));
        await this.InvokeAsync<StockyApi.WatchlistsController, WatchlistDto>(
            c => c.Create(new CreateWatchlistRequest(name)));
        TempData["Status"] = "Watchlist created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddItem(Guid id, string symbol)
    {
        if (!string.IsNullOrWhiteSpace(symbol))
        {
            await this.InvokeAsync<StockyApi.WatchlistsController, WatchlistItemDto>(
                c => c.AddItem(id, new AddWatchlistItemRequest(symbol.Trim().ToUpperInvariant())));
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveItem(Guid id, Guid itemId)
    {
        await this.InvokeRawAsync<StockyApi.WatchlistsController>(c => c.RemoveItem(id, itemId));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        await this.InvokeRawAsync<StockyApi.WatchlistsController>(c => c.Delete(id));
        return RedirectToAction(nameof(Index));
    }
}
