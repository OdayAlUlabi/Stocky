using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stocky.Api.Dtos;
using Stocky.Web.Mvc.Internal;
using Stocky.Web.Mvc.ViewModels;
using StockyApi = Stocky.Api.Controllers;

namespace Stocky.Web.Mvc.Controllers;

[Authorize]
public class StrategyController : Controller
{
    public async Task<IActionResult> Breakdown(Guid? portfolioId = null, CancellationToken ct = default)
    {
        var portfolios = ((await this.InvokeAsync<StockyApi.PortfoliosController, IEnumerable<PortfolioDto>>(
            c => c.List())) ?? Array.Empty<PortfolioDto>()).ToList();

        // When no explicit portfolioId is in the query string, default to "Shared Portfolio" / "Shared"
        if (!portfolioId.HasValue && !Request.Query.ContainsKey("portfolioId"))
        {
            var shared = portfolios.FirstOrDefault(p =>
                p.Name.Equals("Shared Portfolio", StringComparison.OrdinalIgnoreCase) ||
                p.Name.Equals("Shared", StringComparison.OrdinalIgnoreCase));
            if (shared is not null)
                portfolioId = shared.Id;
        }

        var allHoldings = (await this.InvokeAsync<StockyApi.StrategyController, IEnumerable<StrategyHoldingDto>>(
            c => c.ByStrategy(ct)) ?? Array.Empty<StrategyHoldingDto>()).ToList();

        var holdings = portfolioId.HasValue
            ? allHoldings.Where(h => h.PortfolioId == portfolioId.Value).ToList()
            : allHoldings;

        var groups = holdings
            .GroupBy(h => h.Strategy)
            .OrderBy(g => g.Key)
            .ToList();

        var totalMarketValue = holdings
            .Where(h => h.MarketValue.HasValue)
            .Sum(h => h.MarketValue!.Value);

        var vm = new StrategyBreakdownViewModel(
            groups.Cast<IGrouping<string, StrategyHoldingDto>>().ToList(),
            totalMarketValue,
            portfolios.AsReadOnly(),
            portfolioId);

        return View(vm);
    }
}
