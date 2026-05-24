using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stocky.Api.Dtos;
using Stocky.Web.Mvc.Internal;
using StockyApi = Stocky.Api.Controllers;

namespace Stocky.Web.Mvc.Controllers;

[Authorize]
public class HomeController : Controller
{
    public async Task<IActionResult> Index(Guid? portfolioId)
    {
        var dashboard = await this.InvokeAsync<StockyApi.DashboardController, DashboardDto>(
            c => c.Get(portfolioId));
        var portfolios = await this.InvokeAsync<StockyApi.PortfoliosController, IEnumerable<PortfolioDto>>(
            c => c.List()) ?? Array.Empty<PortfolioDto>();
        ViewBag.Portfolios = portfolios.ToList();
        ViewBag.SelectedPortfolioId = portfolioId;
        return View(dashboard);
    }

    [AllowAnonymous]
    public IActionResult Error() => View();
}
