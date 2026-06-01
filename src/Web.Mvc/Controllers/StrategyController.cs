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
    public async Task<IActionResult> Breakdown(CancellationToken ct = default)
    {
        var holdings = (await this.InvokeAsync<StockyApi.StrategyController, IEnumerable<StrategyHoldingDto>>(
            c => c.ByStrategy(ct)) ?? Array.Empty<StrategyHoldingDto>()).ToList();

        var groups = holdings
            .GroupBy(h => h.Strategy)
            .OrderBy(g => g.Key)
            .ToList();

        var totalMarketValue = holdings
            .Where(h => h.MarketValue.HasValue)
            .Sum(h => h.MarketValue!.Value);

        var vm = new StrategyBreakdownViewModel(
            groups.Cast<IGrouping<string, StrategyHoldingDto>>().ToList(),
            totalMarketValue);

        return View(vm);
    }
}
