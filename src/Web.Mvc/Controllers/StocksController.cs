using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;
using Stocky.Api.Dtos;
using Stocky.Web.Mvc.Internal;
using Stocky.Web.Mvc.ViewModels;
using StockyApi = Stocky.Api.Controllers;

namespace Stocky.Web.Mvc.Controllers;

[Authorize]
public class StocksController : Controller
{
    [HttpGet("/stocks/{symbol?}")]
    public async Task<IActionResult> Index(
        string? symbol = "AAPL",
        string timeframe = "1D",
        int historyYears = 5,
        decimal takeProfitPercent = 5m,
        decimal stopLossPercent = 2.5m,
        int timeStopBars = 10,
        bool useTrailingStop = false,
        bool enablePartialExit = false,
        decimal partialExitTargetPercent = 3m,
        decimal partialExitQuantityPercent = 50m,
        decimal maxRiskPerTradePercent = 1m,
        decimal initialCash = 10_000m,
        CancellationToken ct = default)
    {
        symbol = string.IsNullOrWhiteSpace(symbol) ? "AAPL" : symbol.Trim().ToUpperInvariant();
        var strategy = new SingleStockStrategyConfigDto(
            HistoryYears: Math.Clamp(historyYears, 1, 10),
            TakeProfitPercent: takeProfitPercent,
            StopLossPercent: stopLossPercent,
            TimeStopBars: Math.Clamp(timeStopBars, 1, 90),
            UseTrailingStop: useTrailingStop,
            MinimumConditionsToTrigger: 3,
            VolumeConfirmationMultiplier: 1.5m,
            EnablePartialExit: enablePartialExit,
            PartialExitTargetPercent: partialExitTargetPercent,
            PartialExitQuantityPercent: partialExitQuantityPercent,
            MaxRiskPerTradePercent: maxRiskPerTradePercent,
            RoundTripCost: 40m);

        var analysis = await this.InvokeAsync<StockyApi.SingleStockAnalysisController, SingleStockAnalysisDto>(
            c => c.Get(symbol, timeframe, strategy.HistoryYears, ct));
        var backtest = await this.InvokeAsync<StockyApi.SingleStockAnalysisController, SingleStockBacktestDto>(
            c => c.Backtest(symbol, timeframe, strategy, ct));
        var walkForward = await this.InvokeAsync<StockyApi.SingleStockAnalysisController, SingleStockWalkForwardDto>(
            c => c.WalkForward(symbol, timeframe, strategy, ct));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = today.AddYears(-strategy.HistoryYears);
        var bars = (await this.InvokeAsync<StockyApi.BarsController, IEnumerable<OhlcBarDto>>(
            c => c.Get(symbol, from, today, Math.Max(120, strategy.HistoryYears * 252), ct)))?.ToList()
            ?? new List<OhlcBarDto>();

        var tdSequential = await this.InvokeAsync<StockyApi.SingleStockAnalysisController, TdSequentialResultDto>(
            c => c.TdSequential(symbol, timeframe, 100, 250, 24, 70.0, ct));

        var model = new SingleStockDashboardViewModel(
            symbol,
            timeframe,
            strategy,
            analysis ?? new SingleStockAnalysisDto(symbol, timeframe, null, null, from, today, 0, null, null, false, null, new SetupSnapshotDto("Long", "Watching", null, null, null, null, null, null, 0m, Array.Empty<SetupConditionDto>()), strategy, new[] { "No analysis payload returned." }),
            backtest ?? new SingleStockBacktestDto(symbol, timeframe, initialCash, initialCash, 0m, 0m, 0m, 0m, 0m, 0m, 0, 0, 0, 0, 0m, "No backtest payload returned", Array.Empty<SingleStockTradeDto>(), Array.Empty<SingleStockEquityPointDto>(), Array.Empty<string>()),
            walkForward ?? new SingleStockWalkForwardDto(symbol,
                new SingleStockBacktestDto(symbol, timeframe, initialCash, initialCash, 0m, 0m, 0m, 0m, 0m, 0m, 0, 0, 0, 0, 0m, "No walk-forward payload returned", Array.Empty<SingleStockTradeDto>(), Array.Empty<SingleStockEquityPointDto>(), Array.Empty<string>()),
                new SingleStockBacktestDto(symbol, timeframe, initialCash, initialCash, 0m, 0m, 0m, 0m, 0m, 0m, 0, 0, 0, 0, 0m, "No walk-forward payload returned", Array.Empty<SingleStockTradeDto>(), Array.Empty<SingleStockEquityPointDto>(), Array.Empty<string>()),
                "No walk-forward payload returned",
                Array.Empty<string>()),
            bars,
            tdSequential);

        ViewBag.TradingViewSymbol = ResolveTradingViewSymbol(symbol);
        ViewBag.WalkForwardWarnings = model.WalkForward.Warnings;
        return View(model);
    }

    private static string ResolveTradingViewSymbol(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return "NASDAQ:AAPL";
        }

        var normalized = symbol.Trim().ToUpperInvariant();

        // Keep explicit exchange-qualified symbols untouched.
        if (normalized.Contains(':'))
        {
            return normalized;
        }

        // Map FX-style pairs, for example EUR/USD => FX:EURUSD.
        if (normalized.Contains('/'))
        {
            var pair = normalized.Replace("/", string.Empty, StringComparison.Ordinal);
            if (Regex.IsMatch(pair, "^[A-Z]{6}$"))
            {
                return $"FX:{pair}";
            }
        }

        // Common crypto pairs routed to Binance.
        if (Regex.IsMatch(normalized, "^[A-Z0-9]{2,12}USDT?$"))
        {
            return normalized.EndsWith("USDT", StringComparison.Ordinal) ? $"BINANCE:{normalized}" : $"BINANCE:{normalized[..^3]}USDT";
        }

        // Default to US equities.
        return $"NASDAQ:{normalized}";
    }
}