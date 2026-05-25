using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stocky.Api.Dtos;
using Stocky.Web.Mvc.Internal;
using StockyApi = Stocky.Api.Controllers;

namespace Stocky.Web.Mvc.Controllers;

[Authorize]
[Route("Portfolios/{portfolioId:guid}/Cash/{action=Index}/{id?}")]
public class CashController : Controller
{
    public async Task<IActionResult> Index(Guid portfolioId)
    {
        var portfolios = await this.InvokeAsync<StockyApi.PortfoliosController, IEnumerable<PortfolioDto>>(
            c => c.List()) ?? Array.Empty<PortfolioDto>();
        var portfolio = portfolios.FirstOrDefault(p => p.Id == portfolioId);
        if (portfolio is null) return NotFound();

        var rows = await this.InvokeAsync<StockyApi.CashController, IEnumerable<CashTransactionDto>>(
            c => c.List(portfolioId)) ?? Array.Empty<CashTransactionDto>();
        var balances = await this.InvokeAsync<StockyApi.CashController, IEnumerable<CashBalanceDto>>(
            c => c.Balances(portfolioId)) ?? Array.Empty<CashBalanceDto>();
        ViewBag.PortfolioId = portfolioId;
        ViewBag.Portfolio = portfolio;
        ViewBag.Portfolios = portfolios.ToList();
        ViewBag.Balances = balances.ToList();
        return View(rows.ToList());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Guid portfolioId, string type, decimal amount,
        string currency = "USD", DateTimeOffset? executedAt = null, string? notes = null)
    {
        await this.InvokeAsync<StockyApi.CashController, CashTransactionDto>(
            c => c.Create(new CreateCashTransactionRequest(portfolioId, type, amount, currency, executedAt, notes)));
        TempData["Status"] = "Cash transaction added.";
        return RedirectToAction(nameof(Index), new { portfolioId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid portfolioId, Guid id)
    {
        await this.InvokeRawAsync<StockyApi.CashController>(c => c.Delete(id, portfolioId));
        TempData["Status"] = "Cash transaction deleted.";
        return RedirectToAction(nameof(Index), new { portfolioId });
    }
}

[Authorize]
public class NotesController : Controller
{
    public async Task<IActionResult> Index(string? symbol, Guid? portfolioId)
    {
        var rows = await this.InvokeAsync<StockyApi.PositionNotesController, IEnumerable<PositionNoteDto>>(
            c => c.List(symbol, portfolioId)) ?? Array.Empty<PositionNoteDto>();
        ViewBag.Symbol = symbol;
        ViewBag.PortfolioId = portfolioId;
        return View(rows.ToList());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string symbol, string body, Guid? portfolioId)
    {
        if (!string.IsNullOrWhiteSpace(symbol) && !string.IsNullOrWhiteSpace(body))
        {
            await this.InvokeAsync<StockyApi.PositionNotesController, PositionNoteDto>(
                c => c.Create(new CreatePositionNoteRequest(symbol.Trim().ToUpperInvariant(), body, portfolioId)));
        }
        return RedirectToAction(nameof(Index), new { symbol, portfolioId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, string body, string? returnSymbol, Guid? returnPortfolioId)
    {
        await this.InvokeAsync<StockyApi.PositionNotesController, PositionNoteDto>(
            c => c.Update(id, new UpdatePositionNoteRequest(body ?? "")));
        return RedirectToAction(nameof(Index), new { symbol = returnSymbol, portfolioId = returnPortfolioId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, string? returnSymbol, Guid? returnPortfolioId)
    {
        await this.InvokeRawAsync<StockyApi.PositionNotesController>(c => c.Delete(id));
        return RedirectToAction(nameof(Index), new { symbol = returnSymbol, portfolioId = returnPortfolioId });
    }
}

[Authorize]
public class GoalsController : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var rows = await this.InvokeAsync<StockyApi.GoalsController, IEnumerable<GoalDto>>(
            c => c.List(ct)) ?? Array.Empty<GoalDto>();
        return View(rows.ToList());
    }

    [HttpGet]
    public IActionResult Create() => View(new GoalCreateDto(null, "", 0, DateOnly.FromDateTime(DateTime.UtcNow.AddYears(5)), 0, 0.06m));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(GoalCreateDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            ModelState.AddModelError(nameof(dto.Name), "Name is required.");
        if (dto.TargetValue <= 0)
            ModelState.AddModelError(nameof(dto.TargetValue), "Target value must be greater than zero.");
        if (!ModelState.IsValid) return View(dto);
        await this.InvokeAsync<StockyApi.GoalsController, GoalDto>(c => c.Create(dto, ct));
        TempData["Status"] = "Goal created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var g = await this.InvokeAsync<StockyApi.GoalsController, GoalDto>(c => c.Get(id, ct));
        if (g is null) return NotFound();
        ViewBag.Id = id;
        return View(new GoalCreateDto(g.PortfolioId, g.Name, g.TargetValue, g.TargetDate, g.MonthlyContribution, g.ExpectedReturn));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, GoalCreateDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            ModelState.AddModelError(nameof(dto.Name), "Name is required.");
        if (dto.TargetValue <= 0)
            ModelState.AddModelError(nameof(dto.TargetValue), "Target value must be greater than zero.");
        if (!ModelState.IsValid) { ViewBag.Id = id; return View(dto); }
        await this.InvokeAsync<StockyApi.GoalsController, GoalDto>(c => c.Update(id, dto, ct));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await this.InvokeRawAsync<StockyApi.GoalsController>(c => c.Delete(id, ct));
        return RedirectToAction(nameof(Index));
    }
}

[Authorize]
public class TemplatesController : Controller
{
    public IActionResult Index()
    {
        var list = this.Invoke<StockyApi.ModelTemplatesController, IEnumerable<ModelPortfolioTemplateDto>>(
            c => c.List()) ?? Array.Empty<ModelPortfolioTemplateDto>();
        return View(list.ToList());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply(string slug, string portfolioName, string baseCurrency = "USD",
        decimal? initialCashDeposit = null)
    {
        var p = await this.InvokeAsync<StockyApi.ModelTemplatesController, PortfolioDto>(
            c => c.Apply(new ApplyTemplateRequest(slug, portfolioName, baseCurrency, initialCashDeposit)));
        if (p is null) { TempData["Status"] = "Template apply failed."; return RedirectToAction(nameof(Index)); }
        return RedirectToAction("Detail", "Portfolios", new { id = p.Id });
    }
}

[Authorize]
public class AdminController : Controller
{
    public async Task<IActionResult> Audit(int take = 200, string? resource = null)
    {
        var rows = await this.InvokeAsync<StockyApi.AuditController, IEnumerable<AuditEntryDto>>(
            c => c.List(take, resource)) ?? Array.Empty<AuditEntryDto>();
        ViewBag.Take = take;
        ViewBag.Resource = resource;
        return View(rows.ToList());
    }

    [HttpGet]
    public async Task<IActionResult> DataRefresh(CancellationToken ct)
    {
        var refresher = HttpContext.RequestServices.GetRequiredService<Stocky.Api.Services.DataRefreshService>();
        ViewBag.Coverage = await refresher.GetHistoricalCoverageAsync(ct);
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DataRefresh(string scope, CancellationToken ct)
    {
        var refresher = HttpContext.RequestServices.GetRequiredService<Stocky.Api.Services.DataRefreshService>();
        var logger = HttpContext.RequestServices.GetRequiredService<ILogger<AdminController>>();
        object payload = new { scope = scope ?? "all", error = (string?)null };
        IReadOnlyList<Stocky.Api.Services.PortfolioValueSnapshot> portfolios =
            Array.Empty<Stocky.Api.Services.PortfolioValueSnapshot>();
        string status = "Data refresh complete.";
        try
        {
            switch ((scope ?? "all").ToLowerInvariant())
            {
                case "quotes":
                    {
                        var q = await refresher.RefreshQuotesOnceAsync(ct);
                        // Also backfill any missing daily closes from each symbol's earliest
                        // transaction date through today so historical coverage stays complete.
                        var h = await refresher.BackfillHistoricalOnceAsync(ct);
                        portfolios = q.Portfolios;
                        payload = new { scope = "quotes", quotes = q, history = h };
                        break;
                    }
                case "history":
                    {
                        var h = await refresher.BackfillHistoricalOnceAsync(ct);
                        // Even when only running history, refresh portfolio snapshots so the
                        // displayed values match the freshest stored prices.
                        portfolios = await refresher.RefreshPortfolioSnapshotsAsync(ct);
                        payload = new { scope = "history", history = h, portfolios };
                        break;
                    }
                default:
                    {
                        var q = await refresher.RefreshQuotesOnceAsync(ct);
                        var h = await refresher.BackfillHistoricalOnceAsync(ct);
                        portfolios = q.Portfolios;
                        payload = new { scope = "all", quotes = q, history = h };
                        break;
                    }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Admin data refresh failed for scope {Scope}", scope);
            // Surface the inner exception too — for EF SaveChanges failures the
            // outer message is a generic wrapper ("An error occurred while saving
            // the entity changes. See the inner exception for details.") and the
            // real cause (unique-key violation, FK error, etc.) is in InnerException.
            var detail = ex.Message;
            var inner = ex.InnerException;
            int depth = 0;
            while (inner is not null && depth++ < 4)
            {
                detail += $" → {inner.GetType().Name}: {inner.Message}";
                inner = inner.InnerException;
            }
            status = $"Data refresh failed: {detail}. Symbols without provider data are skipped — see container logs for details.";
            payload = new { scope = scope ?? "all", error = detail };
        }
        var jsonOpts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
        TempData["RefreshPayload"] = System.Text.Json.JsonSerializer.Serialize(payload, jsonOpts);
        TempData["PortfolioValues"] = System.Text.Json.JsonSerializer.Serialize(portfolios, jsonOpts);
        try
        {
            TempData["Coverage"] = System.Text.Json.JsonSerializer.Serialize(
                await refresher.GetHistoricalCoverageAsync(ct), jsonOpts);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Admin data refresh: failed to fetch historical coverage");
        }
        TempData["Status"] = status;
        return RedirectToAction(nameof(DataRefresh));
    }
}
