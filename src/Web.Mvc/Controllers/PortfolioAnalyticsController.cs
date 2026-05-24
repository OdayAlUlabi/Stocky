using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stocky.Api.Dtos;
using Stocky.Web.Mvc.Internal;
using StockyApi = Stocky.Api.Controllers;

namespace Stocky.Web.Mvc.Controllers;

/// <summary>
/// Portfolio-scoped analytical views: Holdings, Performance, History, Capital
/// Flow, Capital Gains, Allocation, Reports, Wash Sales, Rebalance, Position
/// Detail, Risk, Benchmark. All routes take <c>portfolioId</c> as the first
/// segment, mirroring the SPA's <c>/portfolios/:id/...</c> structure.
/// </summary>
[Authorize]
[Route("Portfolios/{portfolioId:guid}/[action]")]
public class PortfolioAnalyticsController : Controller
{
    public async Task<IActionResult> Holdings(Guid portfolioId)
    {
        var rows = await this.InvokeAsync<StockyApi.HoldingsController, IEnumerable<HoldingDto>>(
            c => c.List(portfolioId)) ?? Array.Empty<HoldingDto>();
        ViewBag.PortfolioId = portfolioId;
        return View(rows.ToList());
    }

    public async Task<IActionResult> Performance(Guid portfolioId, int days = 90)
    {
        var dto = await this.InvokeAsync<StockyApi.PerformanceController, PerformanceDto>(
            c => c.Get(portfolioId, days));
        await LoadPortfolioContext(portfolioId);
        ViewBag.Days = days;
        return View(dto);
    }

    public async Task<IActionResult> History(Guid portfolioId, CancellationToken ct)
    {
        var dto = await this.InvokeAsync<StockyApi.HistoryController, PortfolioHistoryDto>(
            c => c.Get(portfolioId, ct));
        ViewBag.PortfolioId = portfolioId;
        return View(dto);
    }

    public async Task<IActionResult> CapitalFlow(Guid portfolioId, CancellationToken ct)
    {
        // Capital-flow view re-uses the History DTO (events stream) but renders
        // only the deposit/withdrawal/dividend rows.
        var dto = await this.InvokeAsync<StockyApi.HistoryController, PortfolioHistoryDto>(
            c => c.Get(portfolioId, ct));
        ViewBag.PortfolioId = portfolioId;
        return View(dto);
    }

    public async Task<IActionResult> Analytics(Guid portfolioId, CancellationToken ct)
    {
        var dto = await this.InvokeAsync<StockyApi.AnalyticsController, PortfolioAnalyticsDto>(
            c => c.Get(portfolioId, ct));
        ViewBag.PortfolioId = portfolioId;
        return View(dto);
    }

    public async Task<IActionResult> Allocation(Guid portfolioId)
    {
        var dto = await this.InvokeAsync<StockyApi.AllocationController, AllocationDto>(
            c => c.Get(portfolioId));
        ViewBag.PortfolioId = portfolioId;
        return View(dto);
    }

    public async Task<IActionResult> Reports(Guid portfolioId, DateTimeOffset? from, DateTimeOffset? to, int? year)
    {
        var summary = await this.InvokeAsync<StockyApi.ReportsController, ReportSummaryDto>(
            c => c.Summary(portfolioId, from, to));
        var dividends = await this.InvokeAsync<StockyApi.ReportsController, IEnumerable<DividendRowDto>>(
            c => c.Dividends(portfolioId, year));
        await LoadPortfolioContext(portfolioId);
        ViewBag.Dividends = (dividends ?? Array.Empty<DividendRowDto>()).ToList();
        ViewBag.From = from;
        ViewBag.To = to;
        ViewBag.Year = year;
        return View(summary);
    }

    public async Task<IActionResult> CapitalGains(Guid portfolioId, int? year)
    {
        var dto = await this.InvokeAsync<StockyApi.CapitalGainsController, CapitalGainsDto>(
            c => c.Get(portfolioId, year));
        ViewBag.PortfolioId = portfolioId;
        ViewBag.Year = year;
        return View(dto);
    }

    public async Task<IActionResult> WashSales(Guid portfolioId, int? year, CancellationToken ct)
    {
        var dto = await this.InvokeAsync<StockyApi.WashSalesController, WashSaleReportDto>(
            c => c.Get(portfolioId, year, ct));
        ViewBag.PortfolioId = portfolioId;
        ViewBag.Year = year;
        return View(dto);
    }

    public async Task<IActionResult> Rebalance(Guid portfolioId, CancellationToken ct)
    {
        var report = await this.InvokeAsync<StockyApi.RebalanceController, RebalanceReportDto>(
            c => c.Get(portfolioId, ct));
        var targets = await this.InvokeAsync<StockyApi.RebalanceController, IEnumerable<RebalanceTargetDto>>(
            c => c.GetTargets(portfolioId, ct));
        await LoadPortfolioContext(portfolioId);
        ViewBag.Targets = (targets ?? Array.Empty<RebalanceTargetDto>()).ToList();
        return View(report);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("/Portfolios/{portfolioId:guid}/Rebalance/SetTargets")]
    public async Task<IActionResult> SetTargets(Guid portfolioId, List<RebalanceTargetDto> targets, CancellationToken ct)
    {
        var clean = (targets ?? new()).Where(t => !string.IsNullOrWhiteSpace(t.Symbol)).ToList();
        await this.InvokeAsync<StockyApi.RebalanceController, IEnumerable<RebalanceTargetDto>>(
            c => c.PutTargets(portfolioId, clean, ct));
        TempData["Status"] = "Targets saved.";
        return RedirectToAction(nameof(Rebalance), new { portfolioId });
    }

    public async Task<IActionResult> Risk(Guid portfolioId, CancellationToken ct)
    {
        var dto = await this.InvokeAsync<StockyApi.RiskController, RiskMetricsDto>(
            c => c.Get(portfolioId, ct));
        ViewBag.PortfolioId = portfolioId;
        return View(dto);
    }

    public async Task<IActionResult> Benchmark(Guid portfolioId, string? symbol, CancellationToken ct)
    {
        var dto = await this.InvokeAsync<StockyApi.BenchmarkController, BenchmarkComparisonDto>(
            c => c.Get(portfolioId, symbol, ct));
        ViewBag.PortfolioId = portfolioId;
        ViewBag.Symbol = symbol;
        return View(dto);
    }

    public async Task<IActionResult> Correlation(Guid portfolioId, int days = 90, CancellationToken ct = default)
    {
        var dto = await this.InvokeAsync<StockyApi.CorrelationController, CorrelationDto>(
            c => c.Get(portfolioId, days, ct));
        ViewBag.PortfolioId = portfolioId;
        ViewBag.Days = days;
        return View(dto);
    }

    // =========================================================================
    // Advanced portfolio analytics (GitHub milestone #8) — issues 2.1, 2.2,
    // 2.4, 2.5. Each in-process API call uses the same cookie-auth User so
    // ownership checks behave identically to the JSON endpoints.
    // =========================================================================

    public async Task<IActionResult> VarSuite(
        Guid portfolioId,
        decimal confidence = 0.95m,
        int holdingDays = 1,
        int simulations = 10000,
        CancellationToken ct = default)
    {
        var dto = await this.InvokeAsync<StockyApi.VarSuiteController, VarSuiteDto>(
            c => c.Get(portfolioId, confidence, holdingDays, simulations, ct));
        await LoadPortfolioContext(portfolioId);
        ViewBag.Confidence = confidence;
        ViewBag.HoldingDays = holdingDays;
        ViewBag.Simulations = simulations;
        return View(dto);
    }

    public async Task<IActionResult> StressTest(Guid portfolioId, CancellationToken ct = default)
    {
        var scenarios = this.Invoke<StockyApi.StressTestController, IEnumerable<StressScenarioDto>>(
            c => c.Scenarios());
        await LoadPortfolioContext(portfolioId);
        ViewBag.Scenarios = (scenarios ?? Array.Empty<StressScenarioDto>()).ToList();
        ViewBag.Result = null;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("/Portfolios/{portfolioId:guid}/StressTest/Run")]
    public async Task<IActionResult> StressTestRun(
        Guid portfolioId,
        string? scenarioId,
        decimal? equityShock,
        decimal? ratesShock,
        decimal? usdShock,
        decimal? oilShock,
        decimal? vixShock,
        CancellationToken ct = default)
    {
        StressShockDto? custom = null;
        if (string.IsNullOrWhiteSpace(scenarioId) || scenarioId == "custom")
        {
            custom = new StressShockDto(
                equityShock ?? 0m, ratesShock ?? 0m, usdShock ?? 0m,
                oilShock ?? 0m, vixShock ?? 0m);
            scenarioId = null;
        }
        var req = new StressTestRequest(portfolioId, scenarioId, custom);
        var result = await this.InvokeAsync<StockyApi.StressTestController, StressTestResultDto>(
            c => c.Run(portfolioId, req, ct));
        var scenarios = this.Invoke<StockyApi.StressTestController, IEnumerable<StressScenarioDto>>(
            c => c.Scenarios());
        await LoadPortfolioContext(portfolioId);
        ViewBag.Scenarios = (scenarios ?? Array.Empty<StressScenarioDto>()).ToList();
        ViewBag.Result = result;
        ViewBag.SelectedScenarioId = scenarioId ?? "custom";
        return View(nameof(StressTest));
    }

    public async Task<IActionResult> Liquidity(
        Guid portfolioId,
        decimal participation = 0.20m,
        int thresholdDays = 5,
        int advLookback = 30,
        CancellationToken ct = default)
    {
        var dto = await this.InvokeAsync<StockyApi.LiquidityRiskController, LiquidityRiskDto>(
            c => c.Get(portfolioId, participation, thresholdDays, advLookback, ct));
        await LoadPortfolioContext(portfolioId);
        ViewBag.Participation = participation;
        ViewBag.ThresholdDays = thresholdDays;
        ViewBag.AdvLookback = advLookback;
        return View(dto);
    }

    public async Task<IActionResult> Concentration(
        Guid portfolioId,
        decimal maxPosition = 0.10m,
        decimal maxSector = 0.30m,
        decimal maxCountry = 0.40m,
        CancellationToken ct = default)
    {
        var dto = await this.InvokeAsync<StockyApi.ConcentrationController, ConcentrationRiskDto>(
            c => c.Get(portfolioId, maxPosition, maxSector, maxCountry, ct));
        await LoadPortfolioContext(portfolioId);
        ViewBag.MaxPosition = maxPosition;
        ViewBag.MaxSector = maxSector;
        ViewBag.MaxCountry = maxCountry;
        return View(dto);
    }

    [Route("/Portfolios/{portfolioId:guid}/Positions/{symbol}")]
    public async Task<IActionResult> Position(Guid portfolioId, string symbol)
    {
        var dto = await this.InvokeAsync<StockyApi.PositionDetailController, PositionDetailDto>(
            c => c.Get(portfolioId, symbol));
        if (dto is null) return NotFound();
        ViewBag.PortfolioId = portfolioId;
        return View(dto);
    }

    private async Task LoadPortfolioContext(Guid portfolioId)
    {
        var portfolios = await this.InvokeAsync<StockyApi.PortfoliosController, IEnumerable<PortfolioDto>>(
            c => c.List()) ?? Array.Empty<PortfolioDto>();
        ViewBag.PortfolioId = portfolioId;
        ViewBag.Portfolios = portfolios.ToList();
        ViewBag.Portfolio = portfolios.FirstOrDefault(p => p.Id == portfolioId);
    }
}
