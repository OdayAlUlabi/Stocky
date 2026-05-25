using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stocky.Api.Dtos;
using Stocky.Web.Mvc.Internal;
using StockyApi = Stocky.Api.Controllers;

namespace Stocky.Web.Mvc.Controllers;

/// <summary>
/// Standalone portfolio tools that don't bind to a single portfolio id:
/// momentum scoring across a free-form universe, mean-variance optimizer,
/// and position sizing. Each action POSTs a form to a sibling action that
/// invokes the corresponding API controller in-process.
/// </summary>
[Authorize]
[Route("Tools/[action]")]
public class AdvancedToolsController : Controller
{
    // ---- shared: load portfolios list for the picker ----------------------
    private async Task<List<PortfolioDto>> LoadPortfoliosAsync()
        => (await this.InvokeAsync<StockyApi.PortfoliosController, IEnumerable<PortfolioDto>>(
            c => c.List()) ?? Array.Empty<PortfolioDto>()).ToList();

    private async Task<List<HoldingDto>> LoadHoldingsAsync(Guid portfolioId, CancellationToken ct)
        => (await this.InvokeAsync<StockyApi.HoldingsController, IEnumerable<HoldingDto>>(
            c => c.List(portfolioId, ct)) ?? Array.Empty<HoldingDto>()).ToList();

    // ---- Momentum scoring --------------------------------------------------
    [HttpGet]
    public async Task<IActionResult> Momentum(
        string? symbols, int[]? windows, bool volScale = true, bool skipLatest = true,
        Guid? portfolioId = null, CancellationToken ct = default)
    {
        var portfolioList = await LoadPortfoliosAsync();
        ViewBag.Portfolios = portfolioList;
        ViewBag.PortfolioId = portfolioId;

        // Pre-populate symbols from portfolio holdings when no manual override
        if (portfolioId.HasValue && string.IsNullOrWhiteSpace(symbols))
        {
            var holdings = await LoadHoldingsAsync(portfolioId.Value, ct);
            symbols = string.Join(", ", holdings.Where(h => h.Quantity > 0).Select(h => h.Symbol).Distinct());
            ViewBag.PortfolioName = portfolioList.FirstOrDefault(p => p.Id == portfolioId)?.Name;
        }

        var universe = (symbols ?? "AAPL,MSFT,GOOG,AMZN,META,NVDA,JPM,XOM")
            .Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToUpperInvariant()).Distinct().ToList();
        var wins = (windows is null || windows.Length == 0) ? new[] { 21, 63, 126, 252 } : windows;
        ViewBag.Symbols = string.Join(", ", universe);
        ViewBag.Windows = string.Join(",", wins);
        ViewBag.VolScale = volScale;
        ViewBag.SkipLatest = skipLatest;
        ViewBag.Result = null;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MomentumRun(string symbols, string windows, bool volScale = true, bool skipLatest = true, Guid? portfolioId = null, CancellationToken ct = default)
    {
        var universe = (symbols ?? "")
            .Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToUpperInvariant()).Distinct().ToList();
        var wins = (windows ?? "21,63,126,252")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var n) ? n : 0).Where(n => n > 0).ToList();
        if (wins.Count == 0) wins = new() { 21, 63, 126, 252 };
        var req = new MomentumRequest(universe, wins, volScale, skipLatest);
        var dto = await this.InvokeAsync<StockyApi.MomentumController, MomentumScoreSetDto>(
            c => c.Scores(req, ct));
        var portfolioList = await LoadPortfoliosAsync();
        ViewBag.Portfolios = portfolioList;
        ViewBag.PortfolioId = portfolioId;
        if (portfolioId.HasValue)
            ViewBag.PortfolioName = portfolioList.FirstOrDefault(p => p.Id == portfolioId)?.Name;
        ViewBag.Symbols = string.Join(", ", universe);
        ViewBag.Windows = string.Join(",", wins);
        ViewBag.VolScale = volScale;
        ViewBag.SkipLatest = skipLatest;
        ViewBag.Result = dto;
        return View(nameof(Momentum));
    }

    // ---- Mean-variance optimizer ------------------------------------------
    [HttpGet]
    public async Task<IActionResult> Optimizer(
        string? symbols = null, int lookbackDays = 252, decimal maxWeight = 0.40m,
        decimal minWeight = 0m, decimal riskFreeRate = 0.04m, string estimator = "sample",
        Guid? portfolioId = null, CancellationToken ct = default)
    {
        var portfolioList = await LoadPortfoliosAsync();
        ViewBag.Portfolios = portfolioList;
        ViewBag.PortfolioId = portfolioId;

        if (portfolioId.HasValue && string.IsNullOrWhiteSpace(symbols))
        {
            var holdings = await LoadHoldingsAsync(portfolioId.Value, ct);
            symbols = string.Join(", ", holdings.Where(h => h.Quantity > 0).Select(h => h.Symbol).Distinct());
            ViewBag.PortfolioName = portfolioList.FirstOrDefault(p => p.Id == portfolioId)?.Name;
        }

        ViewBag.Symbols = symbols ?? "AAPL, MSFT, GOOG, AMZN, META";
        ViewBag.LookbackDays = lookbackDays;
        ViewBag.MaxWeight = maxWeight;
        ViewBag.MinWeight = minWeight;
        ViewBag.RiskFreeRate = riskFreeRate;
        ViewBag.Estimator = estimator;
        ViewBag.Result = null;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OptimizerRun(
        string symbols, int lookbackDays = 252, decimal maxWeight = 0.40m,
        decimal minWeight = 0m, decimal riskFreeRate = 0.04m, string estimator = "sample",
        Guid? portfolioId = null, CancellationToken ct = default)
    {
        var list = (symbols ?? "")
            .Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToUpperInvariant()).Distinct().ToList();
        var req = new OptimizerRequest(list, lookbackDays, maxWeight, minWeight, riskFreeRate, estimator);
        var dto = await this.InvokeAsync<StockyApi.OptimizerController, OptimizerResultDto>(
            c => c.Run(req, ct));
        var portfolioList = await LoadPortfoliosAsync();
        ViewBag.Portfolios = portfolioList;
        ViewBag.PortfolioId = portfolioId;
        if (portfolioId.HasValue)
            ViewBag.PortfolioName = portfolioList.FirstOrDefault(p => p.Id == portfolioId)?.Name;
        ViewBag.Symbols = string.Join(", ", list);
        ViewBag.LookbackDays = lookbackDays;
        ViewBag.MaxWeight = maxWeight;
        ViewBag.MinWeight = minWeight;
        ViewBag.RiskFreeRate = riskFreeRate;
        ViewBag.Estimator = estimator;
        ViewBag.Result = dto;
        return View(nameof(Optimizer));
    }

    // ---- Position sizing ---------------------------------------------------
    [HttpGet]
    public async Task<IActionResult> PositionSizing(Guid? portfolioId = null, CancellationToken ct = default)
    {
        var portfolioList = await LoadPortfoliosAsync();
        ViewBag.Portfolios = portfolioList;
        ViewBag.PortfolioId = portfolioId;

        decimal accountSize = 100_000m;
        List<HoldingDto>? holdings = null;

        if (portfolioId.HasValue)
        {
            var perf = await this.InvokeAsync<StockyApi.PortfoliosController, PortfolioPerformanceDto>(
                c => c.Performance(portfolioId.Value));
            if (perf is not null) accountSize = perf.TotalEquity;
            holdings = await LoadHoldingsAsync(portfolioId.Value, ct);
            ViewBag.PortfolioName = portfolioList.FirstOrDefault(p => p.Id == portfolioId)?.Name;
        }

        ViewBag.Holdings = holdings;
        ViewBag.Form = new PositionSizingRequest(
            AccountSize: accountSize, EntryPrice: 100m, StopLossPrice: 95m,
            RiskPerTradePercent: 0.01m,
            WinRate: 0.55m, AvgWinDollars: 200m, AvgLossDollars: 100m,
            AssetVolatilityPercent: 0.25m, TargetVolatilityPercent: 0.10m);
        ViewBag.Result = null;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PositionSizingRun(PositionSizingRequest form, Guid? portfolioId = null, CancellationToken ct = default)
    {
        var portfolioList = await LoadPortfoliosAsync();
        ViewBag.Portfolios = portfolioList;
        ViewBag.PortfolioId = portfolioId;
        if (portfolioId.HasValue)
        {
            var holdings = await LoadHoldingsAsync(portfolioId.Value, ct);
            ViewBag.Holdings = holdings;
            ViewBag.PortfolioName = portfolioList.FirstOrDefault(p => p.Id == portfolioId)?.Name;
        }
        ViewBag.Form = form;
        var dto = this.Invoke<StockyApi.PositionSizingController, PositionSizingResultDto>(
            c => c.Compute(form));
        ViewBag.Result = dto;
        return View(nameof(PositionSizing));
    }
}
