using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stocky.Api.Dtos;
using Stocky.Web.Mvc.Internal;
using StockyApi = Stocky.Api.Controllers;

namespace Stocky.Web.Mvc.Controllers;

[Authorize]
public class PortfoliosController : Controller
{
    // GET /Portfolios
    public async Task<IActionResult> Index()
    {
        var list = await this.InvokeAsync<StockyApi.PortfoliosController, IEnumerable<PortfolioDto>>(
            c => c.List()) ?? Array.Empty<PortfolioDto>();
        return View(list.ToList());
    }

    // GET /Portfolios/Detail/{id}
    public async Task<IActionResult> Detail(Guid id)
    {
        var portfolio = await this.InvokeAsync<StockyApi.PortfoliosController, PortfolioDto>(
            c => c.Get(id));
        if (portfolio is null) return NotFound();

        var holdings = await this.InvokeAsync<StockyApi.HoldingsController, IEnumerable<HoldingDto>>(
            c => c.List(id)) ?? Array.Empty<HoldingDto>();
        var perf = await this.InvokeAsync<StockyApi.PortfoliosController, PortfolioPerformanceDto>(
            c => c.Performance(id));

        ViewBag.Portfolio = portfolio;
        ViewBag.Performance = perf;
        return View(holdings.ToList());
    }

    // GET /Portfolios/Create
    public IActionResult Create() => View(new CreatePortfolioRequest("", "USD", "FIFO"));

    // POST /Portfolios/Create
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreatePortfolioRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            ModelState.AddModelError(nameof(req.Name), "Name is required.");
        if (string.IsNullOrWhiteSpace(req.BaseCurrency))
            ModelState.AddModelError(nameof(req.BaseCurrency), "Base currency is required.");
        if (!ModelState.IsValid) return View(req);
        var created = await this.InvokeAsync<StockyApi.PortfoliosController, PortfolioDto>(
            c => c.Create(req));
        if (created is null)
        {
            ModelState.AddModelError("", "Could not create portfolio.");
            return View(req);
        }
        return RedirectToAction(nameof(Detail), new { id = created.Id });
    }

    // GET /Portfolios/Edit/{id}
    public async Task<IActionResult> Edit(Guid id)
    {
        var p = await this.InvokeAsync<StockyApi.PortfoliosController, PortfolioDto>(c => c.Get(id));
        if (p is null) return NotFound();
        ViewBag.Id = id;
        return View(new UpdatePortfolioRequest(p.Name, p.BaseCurrency, p.CostBasisMethod));
    }

    // POST /Portfolios/Edit/{id}
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, UpdatePortfolioRequest req,
        [FromServices] Stocky.Api.Services.TaxLotService taxLots)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            ModelState.AddModelError(nameof(req.Name), "Name is required.");
        if (string.IsNullOrWhiteSpace(req.BaseCurrency))
            ModelState.AddModelError(nameof(req.BaseCurrency), "Base currency is required.");
        if (!ModelState.IsValid) { ViewBag.Id = id; return View(req); }
        var updated = await this.InvokeAsync<StockyApi.PortfoliosController, PortfolioDto>(
            c => c.Update(id, req, taxLots));
        if (updated is null)
        {
            ViewBag.Id = id;
            ModelState.AddModelError("", "Update failed.");
            return View(req);
        }
        return RedirectToAction(nameof(Detail), new { id });
    }

    // POST /Portfolios/Delete/{id}
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        await this.InvokeRawAsync<StockyApi.PortfoliosController>(c => c.Delete(id));
        return RedirectToAction(nameof(Index));
    }
}
