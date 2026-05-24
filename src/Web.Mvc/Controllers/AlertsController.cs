using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stocky.Api.Dtos;
using Stocky.Web.Mvc.Internal;
using StockyApi = Stocky.Api.Controllers;

namespace Stocky.Web.Mvc.Controllers;

[Authorize]
public class AlertsController : Controller
{
    public async Task<IActionResult> Index(string? status)
    {
        var list = await this.InvokeAsync<StockyApi.AlertsController, IEnumerable<AlertDto>>(
            c => c.List(status)) ?? Array.Empty<AlertDto>();
        ViewBag.Status = status;
        return View(list.ToList());
    }

    public async Task<IActionResult> History(int take = 200)
    {
        var list = await this.InvokeAsync<StockyApi.AlertsController, IEnumerable<AlertEventDto>>(
            c => c.History(take)) ?? Array.Empty<AlertEventDto>();
        return View(list.ToList());
    }

    [HttpGet]
    public IActionResult Create() => View(new CreateAlertRequest("", "above", 0, null, "Price",
        null, null, null, null, null, "Inbox", null));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateAlertRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Symbol))
            ModelState.AddModelError(nameof(req.Symbol), "Symbol is required.");
        if (string.IsNullOrWhiteSpace(req.Type))
            ModelState.AddModelError(nameof(req.Type), "Type is required.");
        if (!ModelState.IsValid) return View(req);
        await this.InvokeAsync<StockyApi.AlertsController, AlertDto>(c => c.Create(req));
        TempData["Status"] = "Alert created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id)
    {
        var list = await this.InvokeAsync<StockyApi.AlertsController, IEnumerable<AlertDto>>(c => c.List(null))
                   ?? Array.Empty<AlertDto>();
        var a = list.FirstOrDefault(x => x.Id == id);
        if (a is null) return NotFound();
        ViewBag.Id = id;
        ViewBag.Symbol = a.Symbol;
        ViewBag.Type = a.Type;
        return View(new UpdateAlertRequest(a.Threshold, a.Status, a.Note, a.Channels, a.WebhookUrl,
            a.IndicatorPeriod, a.KeywordFilter, a.MinSentiment, a.DaysBeforeEarnings));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, UpdateAlertRequest req)
    {
        await this.InvokeAsync<StockyApi.AlertsController, AlertDto>(c => c.Update(id, req));
        TempData["Status"] = "Alert updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        await this.InvokeRawAsync<StockyApi.AlertsController>(c => c.Delete(id));
        TempData["Status"] = "Alert deleted.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Snooze(Guid id, DateTimeOffset untilUtc)
    {
        await this.InvokeAsync<StockyApi.AlertsController, AlertDto>(
            c => c.Snooze(id, new SnoozeAlertRequest(untilUtc)));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reactivate(Guid id)
    {
        await this.InvokeAsync<StockyApi.AlertsController, AlertDto>(c => c.Reactivate(id));
        return RedirectToAction(nameof(Index));
    }
}
