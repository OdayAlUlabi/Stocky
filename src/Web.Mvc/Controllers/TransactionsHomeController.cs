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
