using Stocky.Api.Dtos;

namespace Stocky.Api.Services;

public sealed class SingleStockTradingBotService(
    IMarketDataProvider marketData,
    TechnicalIndicatorService indicators)
{
    public async Task<SingleStockBacktestDto> RunBacktestAsync(
        SingleStockBacktestRequest request,
        CancellationToken ct = default)
    {
        var strategy = request.Strategy ?? new SingleStockStrategyConfigDto();
        var (bars, benchmarkBars, warnings) = await LoadBarsAsync(request.Symbol, strategy.HistoryYears, ct);
        if (bars.Count == 0)
        {
            return EmptyBacktest(request.Symbol, request.Timeframe, request.InitialCash, warnings.Append("No historical bars were returned.").ToList());
        }

        var result = BuildBacktest(request.Symbol, request.Timeframe, request.InitialCash, strategy, bars, benchmarkBars, warnings);
        return result;
    }

    public async Task<SingleStockWalkForwardDto> RunWalkForwardAsync(
        SingleStockWalkForwardRequest request,
        CancellationToken ct = default)
    {
        var strategy = request.Strategy ?? new SingleStockStrategyConfigDto();
        var (bars, benchmarkBars, warnings) = await LoadBarsAsync(request.Symbol, strategy.HistoryYears, ct);
        if (bars.Count < 20)
        {
            var empty = EmptyBacktest(request.Symbol, request.Timeframe, request.InitialCash, warnings.Append("Not enough bars for walk-forward validation.").ToList());
            return new SingleStockWalkForwardDto(request.Symbol, empty, empty, "Insufficient history", warnings);
        }

        var split = Math.Clamp(request.InSamplePercent, 10m, 90m) / 100m;
        var splitIndex = Math.Clamp((int)Math.Floor(bars.Count * split), 10, bars.Count - 10);
        var inSampleBars = bars.Take(splitIndex).ToList();
        var outSampleBars = bars.Skip(splitIndex).ToList();

        var inSampleBench = benchmarkBars.Where(b => b.Date <= inSampleBars[^1].Date).ToList();
        var outSampleBench = benchmarkBars.Where(b => b.Date >= outSampleBars[0].Date).ToList();

        var inSample = BuildBacktest(request.Symbol, request.Timeframe, request.InitialCash, strategy, inSampleBars, inSampleBench, new List<string>());
        var outSample = BuildBacktest(request.Symbol, request.Timeframe, request.InitialCash, strategy, outSampleBars, outSampleBench, new List<string>());

        var verdict = BuildWalkForwardVerdict(inSample, outSample);
        var wfWarnings = warnings.ToList();
        if (inSample.TradeCount < 30 || outSample.TradeCount < 30)
            wfWarnings.Add("Sample size is below 30 trades in at least one split.");

        return new SingleStockWalkForwardDto(request.Symbol, inSample, outSample, verdict, wfWarnings);
    }

    private async Task<(List<DailyBarDto> Bars, List<DailyBarDto> BenchmarkBars, List<string> Warnings)> LoadBarsAsync(
        string symbol,
        int historyYears,
        CancellationToken ct)
    {
        var warnings = new List<string>();
        var cleanSymbol = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = today.AddYears(-Math.Clamp(historyYears, 1, 10));

        var barMap = await marketData.GetDailyBarsAsync(new[] { cleanSymbol, "SPY" }, from, today, ct);
        barMap.TryGetValue(cleanSymbol, out var bars);
        barMap.TryGetValue("SPY", out var benchmark);

        var orderedBars = (bars ?? Array.Empty<DailyBarDto>()).OrderBy(b => b.Date).ToList();
        var orderedBenchmark = (benchmark ?? Array.Empty<DailyBarDto>()).OrderBy(b => b.Date).ToList();

        if (orderedBars.Count == 0)
            warnings.Add($"No bars returned for {cleanSymbol}.");
        if (orderedBenchmark.Count == 0)
            warnings.Add("No benchmark bars returned for SPY.");

        return (orderedBars, orderedBenchmark, warnings);
    }

    private SingleStockBacktestDto BuildBacktest(
        string symbol,
        string timeframe,
        decimal initialCash,
        SingleStockStrategyConfigDto strategy,
        IReadOnlyList<DailyBarDto> bars,
        IReadOnlyList<DailyBarDto> benchmarkBars,
        List<string> warnings)
    {
        if (bars.Count == 0)
            return EmptyBacktest(symbol, timeframe, initialCash, warnings);

        var closes = bars.Select(b => b.Close).ToArray();
        var highs = bars.Select(b => b.High ?? b.Close).ToArray();
        var lows = bars.Select(b => b.Low ?? b.Close).ToArray();
        var opens = bars.Select(b => b.Open ?? b.Close).ToArray();
        var volumes = bars.Select(b => b.Volume ?? 0L).ToArray();

        var sma = indicators.Sma(closes, strategy.TrendSmaPeriod);
        var emaFast = indicators.Ema(closes, 12);
        var emaSlow = indicators.Ema(closes, 26);
        var rsi = indicators.Rsi(closes, strategy.FastRsiPeriod);
        var atr = indicators.Atr(highs, lows, closes, 14);
        var macd = indicators.Macd(closes);

        var benchmarkMap = benchmarkBars.ToDictionary(b => b.Date, b => b.Close);
        decimal benchmarkStart = 0m;
        if (benchmarkBars.Count > 0)
            benchmarkStart = benchmarkBars[0].Close <= 0 ? 1m : benchmarkBars[0].Close;

        decimal cash = initialCash;
        decimal peakEquity = initialCash;
        decimal maxDrawdown = 0m;
        var equityCurve = new List<SingleStockEquityPointDto>(bars.Count);
        var tradeLog = new List<SingleStockTradeDto>();
        var warningsLocal = warnings.ToList();
        var benchmarkFirst = benchmarkStart > 0 ? benchmarkStart : 1m;

        int targetHits = 0, stopHits = 0, expiredTrades = 0;
        decimal grossWins = 0m, grossLosses = 0m, netPnl = 0m;
        var tradeReturns = new List<decimal>();

        var i = Math.Max(strategy.TrendSmaPeriod, Math.Max(strategy.FastRsiPeriod + 1, strategy.SetupLookbackPeriod + 1));
        while (i < bars.Count - 1)
        {
            var trendMet = sma[i].HasValue && closes[i] > sma[i].Value;
            var momentumMet = IsMomentumMet(rsi, macd, i);
            var volumeMet = IsVolumeMet(volumes, i, strategy.VolumeConfirmationMultiplier);
            var triggerMet = IsSupportBounce(bars, strategy.SetupLookbackPeriod, i);
            var metCount = new[] { trendMet, momentumMet, triggerMet, volumeMet }.Count(v => v);

            var earningsBlocked = IsEarningsBlackout(symbol, bars[i].Date, strategy);
            if (metCount >= strategy.MinimumConditionsToTrigger && !earningsBlocked)
            {
                var entryBar = bars[i];
                var entryAtr = atr[i] ?? ((entryBar.High ?? entryBar.Close) - (entryBar.Low ?? entryBar.Close));
                var entryPrice = ApplySlippage(entryBar.Close, strategy.SlippagePercent, true);
                var stopPrice = Math.Round(entryPrice * (1m - strategy.StopLossPercent / 100m), 4);
                var targetPrice = Math.Round(entryPrice * (1m + strategy.TakeProfitPercent / 100m), 4);
                var partialTargetPrice = Math.Round(entryPrice * (1m + strategy.PartialExitTargetPercent / 100m), 4);

                var riskPerShare = entryPrice - stopPrice;
                if (riskPerShare <= 0m)
                {
                    i++;
                    continue;
                }

                var riskBudget = initialCash * (strategy.MaxRiskPerTradePercent / 100m);
                var quantity = Math.Floor(Math.Min(riskBudget / riskPerShare, cash / entryPrice));
                if (quantity <= 0m)
                {
                    i++;
                    continue;
                }

                var reservedCash = quantity * entryPrice;
                cash -= reservedCash;

                var trailingStop = stopPrice;
                var entryDate = entryBar.Date;
                var partialSold = false;
                var partialQty = strategy.EnablePartialExit
                    ? Math.Max(0m, Math.Floor(quantity * (strategy.PartialExitQuantityPercent / 100m)))
                    : 0m;
                if (partialQty >= quantity)
                    partialQty = Math.Floor(quantity / 2m);

                var remainingQty = quantity;
                var tradeClosed = false;
                var exitReason = "Expired";
                decimal exitPrice = closes[i];
                bool gapThroughStop = false;

                for (var j = i + 1; j < bars.Count && !tradeClosed; j++)
                {
                    var bar = bars[j];
                    var barAtr = atr[j] ?? entryAtr;
                    var open = bar.Open ?? bar.Close;
                    var high = bar.High ?? bar.Close;
                    var low = bar.Low ?? bar.Close;
                    if (strategy.UseTrailingStop && closes[j] >= entryPrice * (1m + strategy.TrailingActivationPercent / 100m))
                    {
                        var candidate = Math.Round(closes[j] - (barAtr * strategy.TrailingStopAtrMultiplier), 4);
                        if (candidate > trailingStop)
                            trailingStop = candidate;
                    }

                    if (!partialSold && strategy.EnablePartialExit && partialQty > 0m)
                    {
                        var partialHit = high >= partialTargetPrice;
                        if (partialHit)
                        {
                            var fillPrice = ApplySlippage(partialTargetPrice, strategy.SlippagePercent, false);
                            var gross = (fillPrice - entryPrice) * partialQty;
                            var legNet = gross - (strategy.RoundTripCost * (partialQty / quantity)) / 2m;
                            cash += partialQty * fillPrice;
                            netPnl += legNet;
                            tradeReturns.Add(quantity > 0m ? Math.Round((legNet / (entryPrice * partialQty)) * 100m, 4) : 0m);
                            tradeLog.Add(new SingleStockTradeDto(entryDate, bar.Date, partialQty, entryPrice, fillPrice, "PartialTarget", gross, legNet, quantity > 0m ? Math.Round((legNet / (entryPrice * partialQty)) * 100m, 4) : 0m, true));
                            remainingQty -= partialQty;
                            partialSold = true;
                            targetHits++;
                        }
                    }

                    var stopPriceCandidate = strategy.UseTrailingStop ? trailingStop : stopPrice;
                    if (open <= stopPriceCandidate)
                    {
                        exitPrice = ApplySlippage(open, strategy.SlippagePercent, false);
                        exitReason = strategy.UseTrailingStop ? "TrailingStop" : "StopLoss";
                        gapThroughStop = open < stopPriceCandidate;
                        tradeClosed = true;
                    }
                    else if (low <= stopPriceCandidate)
                    {
                        exitPrice = ApplySlippage(stopPriceCandidate, strategy.SlippagePercent, false);
                        exitReason = strategy.UseTrailingStop ? "TrailingStop" : "StopLoss";
                        tradeClosed = true;
                    }
                    else if (open >= targetPrice)
                    {
                        exitPrice = ApplySlippage(open, strategy.SlippagePercent, false);
                        exitReason = "TargetHit";
                        tradeClosed = true;
                    }
                    else if (high >= targetPrice)
                    {
                        exitPrice = ApplySlippage(targetPrice, strategy.SlippagePercent, false);
                        exitReason = "TargetHit";
                        tradeClosed = true;
                    }
                    else if (j - i >= strategy.TimeStopBars)
                    {
                        exitPrice = ApplySlippage(bar.Close, strategy.SlippagePercent, false);
                        exitReason = "TimeStop";
                        tradeClosed = true;
                    }

                    if (tradeClosed)
                    {
                        var exitQty = remainingQty;
                        var gross = (exitPrice - entryPrice) * exitQty;
                        var legNet = gross - strategy.RoundTripCost * (exitQty / quantity);
                        cash += exitQty * exitPrice;
                        netPnl += legNet;
                        tradeReturns.Add(quantity > 0m ? Math.Round((legNet / (entryPrice * exitQty)) * 100m, 4) : 0m);
                        tradeLog.Add(new SingleStockTradeDto(entryDate, bar.Date, exitQty, entryPrice, exitPrice, exitReason, gross, legNet, quantity > 0m ? Math.Round((legNet / (entryPrice * exitQty)) * 100m, 4) : 0m, false, gapThroughStop));
                        if (exitReason == "TargetHit") targetHits++;
                        else if (exitReason is "StopLoss" or "TrailingStop") stopHits++;
                        else expiredTrades++;
                        if (gross > 0m) grossWins += gross; else grossLosses += Math.Abs(gross);
                        break;
                    }
                }

                if (!tradeClosed)
                {
                    var exitBar = bars[^1];
                    exitPrice = ApplySlippage(exitBar.Close, strategy.SlippagePercent, false);
                    var gross = (exitPrice - entryPrice) * remainingQty;
                    var legNet = gross - strategy.RoundTripCost * (remainingQty / quantity);
                    cash += remainingQty * exitPrice;
                    netPnl += legNet;
                    tradeReturns.Add(quantity > 0m ? Math.Round((legNet / (entryPrice * remainingQty)) * 100m, 4) : 0m);
                    tradeLog.Add(new SingleStockTradeDto(entryDate, exitBar.Date, remainingQty, entryPrice, exitPrice, "Expired", gross, legNet, quantity > 0m ? Math.Round((legNet / (entryPrice * remainingQty)) * 100m, 4) : 0m));
                    expiredTrades++;
                    if (gross > 0m) grossWins += gross; else grossLosses += Math.Abs(gross);
                }

                i += Math.Max(1, strategy.TimeStopBars);
                continue;
            }

            i++;
        }

        for (var idx = 0; idx < bars.Count; idx++)
        {
            var bar = bars[idx];
            var bench = BenchmarkEquity(benchmarkBars, benchmarkFirst, bar.Date);
            var positionValue = 0m;
            var equity = cash + positionValue;
            equityCurve.Add(new SingleStockEquityPointDto(bar.Date, Math.Round(equity, 2), Math.Round(cash, 2), Math.Round(positionValue, 2), Math.Round(bench, 2)));
            if (equity > peakEquity)
                peakEquity = equity;
            if (peakEquity > 0m)
            {
                var dd = (peakEquity - equity) / peakEquity * 100m;
                if (dd > maxDrawdown)
                    maxDrawdown = dd;
            }
        }

        var finalEquity = cash;
        var tradeCount = tradeLog.Count;
        var winRate = tradeCount > 0 ? (decimal)tradeLog.Count(t => t.NetPnL > 0m) / tradeCount * 100m : 0m;
        var profitFactor = grossLosses > 0m ? grossWins / grossLosses : grossWins;
        var expectancy = tradeCount > 0 ? tradeLog.Average(t => t.NetPnL) : 0m;
        var totalReturnPercent = initialCash > 0m ? (finalEquity - initialCash) / initialCash * 100m : 0m;
        var sharpe = ComputeSharpe(equityCurve);
        var verdict = BuildVerdict(tradeCount, expectancy, totalReturnPercent, winRate, equityCurve.Count, warningsLocal);

        return new SingleStockBacktestDto(
            symbol,
            timeframe,
            initialCash,
            Math.Round(finalEquity, 2),
            Math.Round(netPnl, 2),
            Math.Round(totalReturnPercent, 4),
            Math.Round(winRate, 2),
            Math.Round(profitFactor, 4),
            Math.Round(expectancy, 4),
            Math.Round(maxDrawdown, 4),
            tradeCount,
            targetHits,
            stopHits,
            expiredTrades,
            Math.Round(sharpe, 4),
            verdict,
            tradeLog,
            equityCurve,
            warningsLocal);
    }

    private static bool IsMomentumMet(IReadOnlyList<decimal?> rsi, (IReadOnlyList<decimal?> Line, IReadOnlyList<decimal?> Signal, IReadOnlyList<decimal?> Histogram) macd, int index)
    {
        var rsiTurningUp = index > 0 && rsi[index] is { } current && rsi[index - 1] is { } previous && previous < 35m && current > previous;
        var macdBullishCross = index > 0 && macd.Line[index] is { } macdCurrent && macd.Signal[index] is { } signalCurrent && macd.Line[index - 1] is { } macdPrev && macd.Signal[index - 1] is { } signalPrev && macdPrev <= signalPrev && macdCurrent > signalCurrent;
        return rsiTurningUp || macdBullishCross;
    }

    private static bool IsVolumeMet(IReadOnlyList<long> volumes, int index, decimal multiplier)
    {
        var lookback = Math.Min(20, index);
        if (lookback <= 0) return false;
        var avg = volumes.Skip(index - lookback).Take(lookback).Average(v => (decimal)v);
        return avg > 0m && volumes[index] >= avg * multiplier;
    }

    private static bool IsSupportBounce(IReadOnlyList<DailyBarDto> bars, int lookback, int index)
    {
        if (index < 2) return false;
        var start = Math.Max(0, index - lookback);
        var support = bars.Skip(start).Take(index - start).Select(b => b.Low ?? b.Close).DefaultIfEmpty(bars[index].Low ?? bars[index].Close).Min();
        var bar = bars[index];
        var open = bar.Open ?? bar.Close;
        return bar.Low <= support * 1.01m && bar.Close > open;
    }

    private bool IsEarningsBlackout(string symbol, DateOnly date, SingleStockStrategyConfigDto strategy)
    {
        var from = date.AddDays(-Math.Max(1, strategy.EarningsBlockDaysBefore));
        var to = date.AddDays(Math.Max(1, strategy.EarningsBlockDaysAfter));
        var items = marketData.GetEarningsAsync(from, to).GetAwaiter().GetResult();
        return items.Any(e => string.Equals(e.Symbol, symbol, StringComparison.OrdinalIgnoreCase) && date >= e.Date.AddDays(-strategy.EarningsBlockDaysBefore) && date <= e.Date.AddDays(strategy.EarningsBlockDaysAfter));
    }

    private static decimal ApplySlippage(decimal price, decimal slippagePercent, bool adverseForEntry)
    {
        if (slippagePercent <= 0m) return price;
        var factor = slippagePercent / 100m;
        return Math.Round(adverseForEntry ? price * (1m + factor) : price * (1m - factor), 4);
    }

    private static decimal BenchmarkEquity(IReadOnlyList<DailyBarDto> benchmarkBars, decimal benchmarkStart, DateOnly date)
    {
        if (benchmarkBars.Count == 0 || benchmarkStart <= 0m) return 0m;
        var current = benchmarkBars.Where(b => b.Date <= date).OrderByDescending(b => b.Date).FirstOrDefault();
        if (current is null || current.Close <= 0m) return 0m;
        return Math.Round(10_000m * (current.Close / benchmarkStart), 2);
    }

    private static decimal ComputeSharpe(IReadOnlyList<SingleStockEquityPointDto> curve)
    {
        var returns = new List<double>();
        for (var i = 1; i < curve.Count; i++)
        {
            var prev = curve[i - 1].Equity;
            if (prev <= 0m) continue;
            returns.Add((double)((curve[i].Equity - prev) / prev));
        }
        if (returns.Count < 2) return 0m;
        var mean = returns.Average();
        var variance = returns.Sum(r => Math.Pow(r - mean, 2)) / (returns.Count - 1);
        var stdev = Math.Sqrt(variance);
        return stdev > 0 ? (decimal)((mean / stdev) * Math.Sqrt(252)) : 0m;
    }

    private static string BuildVerdict(int tradeCount, decimal expectancy, decimal totalReturnPercent, decimal winRate, int curvePoints, IReadOnlyList<string> warnings)
    {
        var baseVerdict = expectancy > 0m && totalReturnPercent > 0m
            ? $"Backtest shows positive expectancy ({expectancy:F2} net per trade) over {tradeCount} trades."
            : $"Backtest does not show a durable positive expectancy yet ({expectancy:F2} net per trade over {tradeCount} trades).";

        if (tradeCount < 30)
            baseVerdict += " Sample size is small.";
        if (warnings.Count > 0)
            baseVerdict += " Review data-quality warnings before trusting the result.";
        return baseVerdict;
    }

    private static SingleStockBacktestDto EmptyBacktest(string symbol, string timeframe, decimal initialCash, List<string> warnings)
        => new(symbol, timeframe, initialCash, initialCash, 0m, 0m, 0m, 0m, 0m, 0m, 0, 0, 0, 0, 0m, "Insufficient data", Array.Empty<SingleStockTradeDto>(), Array.Empty<SingleStockEquityPointDto>(), warnings);

    private static string BuildWalkForwardVerdict(SingleStockBacktestDto inSample, SingleStockBacktestDto outSample)
    {
        if (inSample.NetPnL > 0m && outSample.NetPnL > 0m)
            return "Both in-sample and out-of-sample periods were profitable, but single-stock regimes can still shift quickly.";
        if (inSample.NetPnL > 0m && outSample.NetPnL <= 0m)
            return "In-sample was profitable, but out-of-sample weakened or turned negative. The setup may be overfit.";
        if (outSample.NetPnL > 0m)
            return "Out-of-sample was positive, but the in-sample period was weaker. Treat the strategy as tentative.";
        return "Walk-forward validation does not yet show stable profitability across unseen data.";
    }
}