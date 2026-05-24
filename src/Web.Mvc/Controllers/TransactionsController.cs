using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stocky.Api.Dtos;
using Stocky.Web.Mvc.Internal;
using StockyApi = Stocky.Api.Controllers;

namespace Stocky.Web.Mvc.Controllers;

[Authorize]
[Route("Portfolios/{portfolioId:guid}/Transactions/{action=Index}/{id?}")]
public class TransactionsController : Controller
{
    public async Task<IActionResult> Index(Guid portfolioId)
    {
        var rows = await this.InvokeAsync<StockyApi.TransactionsController, IEnumerable<TransactionDto>>(
            c => c.List(portfolioId)) ?? Array.Empty<TransactionDto>();
        var portfolios = await this.InvokeAsync<StockyApi.PortfoliosController, IEnumerable<PortfolioDto>>(
            c => c.List()) ?? Array.Empty<PortfolioDto>();
        ViewBag.PortfolioId = portfolioId;
        ViewBag.Portfolios = portfolios.ToList();
        ViewBag.Portfolio = portfolios.FirstOrDefault(p => p.Id == portfolioId);
        return View(rows.ToList());
    }

    [HttpGet]
    public IActionResult Create(Guid portfolioId)
    {
        ViewBag.PortfolioId = portfolioId;
        return View(new CreateTransactionRequest(null, "Buy", 0, 0, 0, "USD", DateTimeOffset.UtcNow, null));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Guid portfolioId, CreateTransactionRequest req)
    {
        if (!ModelState.IsValid) { ViewBag.PortfolioId = portfolioId; return View(req); }
        var created = await this.InvokeAsync<StockyApi.TransactionsController, TransactionDto>(
            c => c.Create(portfolioId, req));
        if (created is null)
        {
            ViewBag.PortfolioId = portfolioId;
            ModelState.AddModelError("", "Could not create transaction.");
            return View(req);
        }
        TempData["Status"] = "Transaction created.";
        return RedirectToAction(nameof(Index), new { portfolioId });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid portfolioId, Guid id)
    {
        var rows = await this.InvokeAsync<StockyApi.TransactionsController, IEnumerable<TransactionDto>>(
            c => c.List(portfolioId)) ?? Array.Empty<TransactionDto>();
        var t = rows.FirstOrDefault(r => r.Id == id);
        if (t is null) return NotFound();
        ViewBag.PortfolioId = portfolioId;
        ViewBag.Id = id;
        return View(new CreateTransactionRequest(t.Symbol, t.Type, t.Quantity, t.Price, t.Fee, t.Currency, t.ExecutedAt, t.Notes));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid portfolioId, Guid id, CreateTransactionRequest req)
    {
        if (!ModelState.IsValid) { ViewBag.PortfolioId = portfolioId; ViewBag.Id = id; return View(req); }
        var updated = await this.InvokeAsync<StockyApi.TransactionsController, TransactionDto>(
            c => c.Update(portfolioId, id, req));
        if (updated is null)
        {
            ViewBag.PortfolioId = portfolioId; ViewBag.Id = id;
            ModelState.AddModelError("", "Update failed.");
            return View(req);
        }
        TempData["Status"] = "Transaction updated.";
        return RedirectToAction(nameof(Index), new { portfolioId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid portfolioId, Guid id)
    {
        await this.InvokeRawAsync<StockyApi.TransactionsController>(c => c.Delete(portfolioId, id));
        TempData["Status"] = "Transaction deleted.";
        return RedirectToAction(nameof(Index), new { portfolioId });
    }

    [HttpGet]
    public IActionResult Import(Guid portfolioId)
    {
        ViewBag.PortfolioId = portfolioId;
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequestFormLimits(MultipartBodyLengthLimit = 25_000_000)]
    public async Task<IActionResult> Import(Guid portfolioId, IFormFile file)
    {
        if (file is null || file.Length == 0)
        {
            ModelState.AddModelError("", "Choose a CSV file to upload.");
            ViewBag.PortfolioId = portfolioId;
            return View();
        }
        var result = await this.InvokeAsync<StockyApi.TransactionImportController, ImportResultDto>(
            c => c.Import(portfolioId, file));
        TempData["Status"] = result is null
            ? "Import failed."
            : $"Imported {result.Imported}, skipped {result.Skipped}.";
        return RedirectToAction(nameof(Index), new { portfolioId });
    }
}
