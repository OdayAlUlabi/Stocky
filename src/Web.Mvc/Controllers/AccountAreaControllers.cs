using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stocky.Api.Dtos;
using Stocky.Web.Mvc.Internal;
using StockyApi = Stocky.Api.Controllers;

namespace Stocky.Web.Mvc.Controllers;

[Authorize]
public class SettingsController : Controller
{
    public async Task<IActionResult> Index()
    {
        var s = await this.InvokeAsync<StockyApi.SettingsController, UserSettingsDto>(c => c.Get());
        return View(s ?? new UserSettingsDto("USD", "system", "en-US", false, false));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(UserSettingsDto dto)
    {
        await this.InvokeAsync<StockyApi.SettingsController, UserSettingsDto>(c => c.Put(dto));
        TempData["Status"] = "Settings saved.";
        return RedirectToAction(nameof(Index));
    }
}

[Authorize]
public class ApiKeysController : Controller
{
    // GET /ApiKeys
    public async Task<IActionResult> Index()
    {
        var keys = await this.InvokeAsync<StockyApi.ApiKeysController, IEnumerable<ApiKeyDto>>(
            c => c.List()) ?? Array.Empty<ApiKeyDto>();
        return View(keys.ToList());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, string? scopes, DateTimeOffset? expiresAt)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Status"] = "Name is required.";
            return RedirectToAction(nameof(Index));
        }
        var created = await this.InvokeAsync<StockyApi.ApiKeysController, CreatedApiKeyDto>(
            c => c.Create(new CreateApiKeyRequest(name, scopes, expiresAt)));
        if (created is not null)
        {
            // Plaintext is shown ONCE — stash in TempData so the redirected list page can render it.
            TempData["NewKeyName"] = created.Key.Name;
            TempData["NewKeyPlaintext"] = created.Plaintext;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(Guid id)
    {
        await this.InvokeRawAsync<StockyApi.ApiKeysController>(c => c.Revoke(id));
        TempData["Status"] = "API key revoked.";
        return RedirectToAction(nameof(Index));
    }
}

[Authorize]
public class ShareController : Controller
{
    // GET /Share
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var rows = await this.InvokeAsync<StockyApi.ShareTokensController, IEnumerable<ShareTokenDto>>(
            c => c.List(ct)) ?? Array.Empty<ShareTokenDto>();
        return View(rows.ToList());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Guid portfolioId, string? label, DateTimeOffset? expiresAt,
        bool includeTransactions, bool includeCostBasis, CancellationToken ct)
    {
        var created = await this.InvokeAsync<StockyApi.ShareTokensController, ShareTokenDto>(
            c => c.Create(new CreateShareTokenRequest(portfolioId, label, expiresAt, includeTransactions, includeCostBasis), ct));
        if (created is not null)
        {
            TempData["NewShareUrl"] = created.ShareUrl;
            TempData["NewShareToken"] = created.Token;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        await this.InvokeRawAsync<StockyApi.ShareTokensController>(c => c.Revoke(id, ct));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await this.InvokeRawAsync<StockyApi.ShareTokensController>(c => c.Delete(id, ct));
        return RedirectToAction(nameof(Index));
    }
}

/// <summary>
/// Anonymous /share/{token} viewer — the public read-only view of a shared
/// portfolio. Mirrors SPA route /share/:token.
/// </summary>
[AllowAnonymous]
[Route("share/{token}")]
public class PublicShareController : Controller
{
    public async Task<IActionResult> Index(string token, CancellationToken ct)
    {
        var dto = await this.InvokeAsync<StockyApi.PublicShareController, SharedPortfolioDto>(
            c => c.Get(token, ct));
        if (dto is null) return NotFound();
        ViewBag.Token = token;
        return View(dto);
    }
}

[Authorize]
public class ReportSchedulesController : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var rows = await this.InvokeAsync<StockyApi.ReportSchedulesController, IEnumerable<ReportScheduleDto>>(
            c => c.List(ct)) ?? Array.Empty<ReportScheduleDto>();
        return View(rows.ToList());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Guid portfolioId, string type, string format,
        string cadence, string? email, bool enabled, CancellationToken ct)
    {
        await this.InvokeAsync<StockyApi.ReportSchedulesController, ReportScheduleDto>(
            c => c.Create(new CreateReportScheduleRequest(portfolioId, type, format, cadence, email, enabled), ct));
        TempData["Status"] = "Schedule created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await this.InvokeRawAsync<StockyApi.ReportSchedulesController>(c => c.Delete(id, ct));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Run(Guid id, CancellationToken ct)
    {
        await this.InvokeAsync<StockyApi.ReportSchedulesController, ReportDeliveryDto>(c => c.Run(id, ct));
        TempData["Status"] = "Report generated.";
        return RedirectToAction(nameof(Index));
    }
}
