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
        var portfolios = ((await this.InvokeAsync<StockyApi.PortfoliosController, IEnumerable<PortfolioDto>>(
            c => c.List())) ?? Array.Empty<PortfolioDto>()).ToList();

        // Auto-select the "Shared" portfolio when no explicit selection is made
        if (portfolioId is null)
        {
            var shared = portfolios.FirstOrDefault(p =>
                p.Name.Contains("shared", StringComparison.OrdinalIgnoreCase));
            if (shared is not null)
                portfolioId = shared.Id;
        }

        var dashboard = await this.InvokeAsync<StockyApi.DashboardController, DashboardDto>(
            c => c.Get(portfolioId));
        ViewBag.Portfolios = portfolios;
        ViewBag.SelectedPortfolioId = portfolioId;
        return View(dashboard);
    }

    [AllowAnonymous]
    public IActionResult Error() => View();
}
