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
    // GET /Transactions  -> redirect to first portfolio's transactions, or to /Portfolios if none.
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var list = await this.InvokeAsync<StockyApi.PortfoliosController, IEnumerable<PortfolioDto>>(
            c => c.List()) ?? Array.Empty<PortfolioDto>();
        var first = list.FirstOrDefault();
        if (first is null)
        {
            TempData["Status"] = "Create a portfolio to start adding transactions.";
            return Redirect("/Portfolios");
        }
        return Redirect($"/Portfolios/{first.Id}/Transactions");
    }
}

[Authorize]
[Route("Cash")]
public class CashHomeController : Controller
{
    // GET /Cash  -> redirect to first portfolio's cash, or to /Portfolios if none.
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var list = await this.InvokeAsync<StockyApi.PortfoliosController, IEnumerable<PortfolioDto>>(
            c => c.List()) ?? Array.Empty<PortfolioDto>();
        var first = list.FirstOrDefault();
        if (first is null)
        {
            TempData["Status"] = "Create a portfolio to start tracking cash.";
            return Redirect("/Portfolios");
        }
        return Redirect($"/Portfolios/{first.Id}/Cash");
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
    public Task<IActionResult> Performance() => RedirectToFirstPortfolio("Performance", "Create a portfolio to view performance.");

    [HttpGet("Reports")]
    public Task<IActionResult> Reports() => RedirectToFirstPortfolio("Reports", "Create a portfolio to view reports.");

    [HttpGet("Rebalance")]
    public Task<IActionResult> Rebalance() => RedirectToFirstPortfolio("Rebalance", "Create a portfolio to set rebalance targets.");

    private async Task<IActionResult> RedirectToFirstPortfolio(string action, string emptyMessage)
    {
        var list = await this.InvokeAsync<StockyApi.PortfoliosController, IEnumerable<PortfolioDto>>(
            c => c.List()) ?? Array.Empty<PortfolioDto>();
        var first = list.FirstOrDefault();
        if (first is null)
        {
            TempData["Status"] = emptyMessage;
            return Redirect("/Portfolios");
        }
        return Redirect($"/Portfolios/{first.Id}/{action}");
    }
}
