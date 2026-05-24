using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stocky.Api.Dtos;
using Stocky.Web.Mvc.Internal;
using StockyApi = Stocky.Api.Controllers;

namespace Stocky.Web.Mvc.Controllers;

[Authorize]
public class NewsController : Controller
{
    public async Task<IActionResult> Index(string? symbols, int limit = 20, CancellationToken ct = default)
    {
        var rows = await this.InvokeAsync<StockyApi.NewsController, IEnumerable<NewsItemDto>>(
            c => c.Get(symbols, limit, ct)) ?? Array.Empty<NewsItemDto>();
        ViewBag.Symbols = symbols;
        ViewBag.Limit = limit;
        return View(rows.ToList());
    }
}

[Authorize]
public class EarningsController : Controller
{
    public async Task<IActionResult> Index(DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var rows = await this.InvokeAsync<StockyApi.EarningsController, IEnumerable<EarningsEventDto>>(
            c => c.Get(from, to, ct)) ?? Array.Empty<EarningsEventDto>();
        ViewBag.From = from;
        ViewBag.To = to;
        return View(rows.ToList());
    }
}

[Authorize]
public class CalendarController : Controller
{
    public async Task<IActionResult> Economic(DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var rows = await this.InvokeAsync<StockyApi.EconomicCalendarController, IEnumerable<EconomicEventDto>>(
            c => c.Get(from, to, ct)) ?? Array.Empty<EconomicEventDto>();
        ViewBag.From = from;
        ViewBag.To = to;
        return View(rows.ToList());
    }
}

[Authorize]
public class ScreenerController : Controller
{
    public async Task<IActionResult> Index(
        string? q,
        string? assetClass,
        string? sector,
        string? country,
        decimal? minMarketCap,
        decimal? maxMarketCap,
        decimal? minDividendYield,
        decimal? maxBeta,
        string? sort = "marketcap-desc",
        int limit = 50)
    {
        var dto = await this.InvokeAsync<StockyApi.SecuritiesController, ScreenerResultDto>(
            c => c.Screener(q, assetClass, sector, country, minMarketCap, maxMarketCap,
                minDividendYield, maxBeta, sort, limit));
        ViewBag.Query = q;
        ViewBag.Sort = sort;
        ViewBag.Limit = limit;
        return View(dto);
    }

    public async Task<IActionResult> Search(string? q, int limit = 10)
    {
        var rows = await this.InvokeAsync<StockyApi.SecuritiesController, IEnumerable<InstrumentDto>>(
            c => c.Search(q, limit)) ?? Array.Empty<InstrumentDto>();
        ViewBag.Query = q;
        return View(rows.ToList());
    }
}
