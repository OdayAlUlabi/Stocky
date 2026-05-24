using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stocky.Api.Dtos;
using Stocky.Web.Mvc.Internal;
using StockyApi = Stocky.Api.Controllers;

namespace Stocky.Web.Mvc.Controllers;

[Authorize]
[Route("Transactions")]
public class TransactionsHomeController : Controller
{
    // GET /Transactions  -> if 1 portfolio, jump straight in; if many, show a chooser; if none, prompt to create one.
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var list = (await this.InvokeAsync<StockyApi.PortfoliosController, IEnumerable<PortfolioDto>>(
            c => c.List()) ?? Array.Empty<PortfolioDto>()).ToList();
        if (list.Count == 0)
        {
            TempData["Status"] = "Create a portfolio to start adding transactions.";
            return Redirect("/Portfolios");
        }
        if (list.Count == 1) return Redirect($"/Portfolios/{list[0].Id}/Transactions");
        ViewBag.Portfolios = list;
        ViewBag.Kind = "Transactions";
        ViewBag.PathTemplate = "/Portfolios/{0}/Transactions";
        ViewBag.Icon = "bi-arrow-left-right";
        return View("~/Views/Shared/PortfolioChooser.cshtml");
    }
}

[Authorize]
[Route("Cash")]
public class CashHomeController : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var list = (await this.InvokeAsync<StockyApi.PortfoliosController, IEnumerable<PortfolioDto>>(
            c => c.List()) ?? Array.Empty<PortfolioDto>()).ToList();
        if (list.Count == 0)
        {
            TempData["Status"] = "Create a portfolio to start tracking cash.";
            return Redirect("/Portfolios");
        }
        if (list.Count == 1) return Redirect($"/Portfolios/{list[0].Id}/Cash");
        ViewBag.Portfolios = list;
        ViewBag.Kind = "Cash";
        ViewBag.PathTemplate = "/Portfolios/{0}/Cash";
        ViewBag.Icon = "bi-cash-coin";
        return View("~/Views/Shared/PortfolioChooser.cshtml");
    }
}

/// <summary>
/// Sidebar shortcuts for the three portfolio-scoped analytics views that the
/// nav exposes without a portfolio id. Each redirects to the first portfolio's
/// equivalent page, or back to /Portfolios when the user has none yet.
/// </summary>
[Authorize]
[Route("PortfolioAnalytics")]
public class PortfolioAnalyticsHomeController : Controller
{
    [HttpGet("Performance")]
    public Task<IActionResult> Performance() => ChoosePortfolio("Performance", "bi-bar-chart-line", "Create a portfolio to view performance.");

    [HttpGet("Reports")]
    public Task<IActionResult> Reports() => ChoosePortfolio("Reports", "bi-file-earmark-bar-graph", "Create a portfolio to view reports.");

    [HttpGet("Rebalance")]
    public Task<IActionResult> Rebalance() => ChoosePortfolio("Rebalance", "bi-sliders", "Create a portfolio to set rebalance targets.");

    private async Task<IActionResult> ChoosePortfolio(string action, string icon, string emptyMessage)
    {
        var list = (await this.InvokeAsync<StockyApi.PortfoliosController, IEnumerable<PortfolioDto>>(
            c => c.List()) ?? Array.Empty<PortfolioDto>()).ToList();
        if (list.Count == 0)
        {
            TempData["Status"] = emptyMessage;
            return Redirect("/Portfolios");
        }
        if (list.Count == 1) return Redirect($"/Portfolios/{list[0].Id}/{action}");
        ViewBag.Portfolios = list;
        ViewBag.Kind = action;
        ViewBag.PathTemplate = $"/Portfolios/{{0}}/{action}";
        ViewBag.Icon = icon;
        return View("~/Views/Shared/PortfolioChooser.cshtml");
    }
}
