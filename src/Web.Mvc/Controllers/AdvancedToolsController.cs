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
    // ---- Momentum scoring --------------------------------------------------
    [HttpGet]
    public IActionResult Momentum(string? symbols, int[]? windows, bool volScale = true, bool skipLatest = true)
    {
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
    public async Task<IActionResult> MomentumRun(string symbols, string windows, bool volScale = true, bool skipLatest = true, CancellationToken ct = default)
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
        ViewBag.Symbols = string.Join(", ", universe);
        ViewBag.Windows = string.Join(",", wins);
        ViewBag.VolScale = volScale;
        ViewBag.SkipLatest = skipLatest;
        ViewBag.Result = dto;
        return View(nameof(Momentum));
    }

    // ---- Mean-variance optimizer ------------------------------------------
    [HttpGet]
    public IActionResult Optimizer()
    {
        ViewBag.Symbols = "AAPL, MSFT, GOOG, AMZN, META";
        ViewBag.LookbackDays = 252;
        ViewBag.MaxWeight = 0.40m;
        ViewBag.MinWeight = 0.0m;
        ViewBag.RiskFreeRate = 0.04m;
        ViewBag.Estimator = "sample";
        ViewBag.Result = null;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OptimizerRun(
        string symbols, int lookbackDays = 252, decimal maxWeight = 0.40m,
        decimal minWeight = 0m, decimal riskFreeRate = 0.04m, string estimator = "sample",
        CancellationToken ct = default)
    {
        var list = (symbols ?? "")
            .Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToUpperInvariant()).Distinct().ToList();
        var req = new OptimizerRequest(list, lookbackDays, maxWeight, minWeight, riskFreeRate, estimator);
        var dto = await this.InvokeAsync<StockyApi.OptimizerController, OptimizerResultDto>(
            c => c.Run(req, ct));
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
    public IActionResult PositionSizing()
    {
        ViewBag.Form = new PositionSizingRequest(
            AccountSize: 100_000m, EntryPrice: 100m, StopLossPrice: 95m,
            RiskPerTradePercent: 0.01m,
            WinRate: 0.55m, AvgWinDollars: 200m, AvgLossDollars: 100m,
            AssetVolatilityPercent: 0.25m, TargetVolatilityPercent: 0.10m);
        ViewBag.Result = null;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult PositionSizingRun(PositionSizingRequest form)
    {
        ViewBag.Form = form;
        var dto = this.Invoke<StockyApi.PositionSizingController, PositionSizingResultDto>(
            c => c.Compute(form));
        ViewBag.Result = dto;
        return View(nameof(PositionSizing));
    }
}
