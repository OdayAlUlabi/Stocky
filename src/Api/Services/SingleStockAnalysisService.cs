using Stocky.Api.Dtos;

namespace Stocky.Api.Services;

/// <summary>
/// Initial symbol-centric analysis slice for the trading bot workflow.
/// This first pass reuses the existing market-data provider stack plus
/// TechnicalIndicatorService to produce a daily setup snapshot.
/// </summary>
public sealed class SingleStockAnalysisService(
    IMarketDataProvider marketData,
    TechnicalIndicatorService indicators)
{
    public async Task<SingleStockAnalysisDto> BuildAsync(
        SingleStockAnalysisRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Symbol))
            throw new ArgumentException("Symbol is required.", nameof(request));

        var symbol = request.Symbol.Trim().ToUpperInvariant();
        var strategy = request.Strategy ?? new SingleStockStrategyConfigDto();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = today.AddYears(-Math.Clamp(strategy.HistoryYears, 1, 10));
        var to = today;

        var barsBySymbol = await marketData.GetDailyBarsAsync(new[] { symbol }, from, to, ct);
        barsBySymbol.TryGetValue(symbol, out var bars);
        bars ??= Array.Empty<DailyBarDto>();

        var warnings = new List<string>();
        if (bars.Count == 0)
        {
            warnings.Add("No historical bars were returned for the selected symbol.");
            return new SingleStockAnalysisDto(
                symbol,
                request.Timeframe,
                null,
                null,
                from,
                to,
                0,
                null,
                null,
                false,
                null,
                new SetupSnapshotDto("Long", "Watching", null, null, null, null, null, null, 0m, Array.Empty<SetupConditionDto>()),
                strategy,
                warnings);
        }

        var orderedBars = bars.OrderBy(b => b.Date).ToList();
        var closes = orderedBars.Select(b => b.Close).ToArray();
        var volumes = orderedBars.Select(b => b.Volume ?? 0L).ToArray();
        var lastBar = orderedBars[^1];

        var quotes = await marketData.GetQuotesAsync(new[] { symbol }, ct);
        var quote = quotes.FirstOrDefault(q => string.Equals(q.Symbol, symbol, StringComparison.OrdinalIgnoreCase));

        var sma = indicators.Sma(closes, strategy.TrendSmaPeriod);
        var rsi = indicators.Rsi(closes, strategy.FastRsiPeriod);
        var lastSma = sma.LastOrDefault();
        var lastRsi = rsi.LastOrDefault();

        var trendMet = lastSma.HasValue && lastBar.Close > lastSma.Value;
        var momentumMet = lastRsi.HasValue && lastRsi.Value < 35m;
        var volumeAverage = orderedBars.Count >= 20
            ? volumes.Skip(Math.Max(0, volumes.Length - 20)).Average(v => (decimal)v)
            : volumes.Length == 0 ? 0m : volumes.Average(v => (decimal)v);
        var latestVolume = lastBar.Volume.HasValue ? lastBar.Volume.Value : 0L;
        var volumeMet = volumeAverage > 0m && latestVolume >= volumeAverage * strategy.VolumeConfirmationMultiplier;
        var setupTriggerMet = false;

        var nextEarnings = (await marketData.GetEarningsAsync(today.AddDays(-strategy.EarningsBlockDaysAfter), today.AddDays(60), ct))
            .Where(e => string.Equals(e.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Date)
            .FirstOrDefault();
        var earningsBlackout = nextEarnings is not null
            && today >= nextEarnings.Date.AddDays(-strategy.EarningsBlockDaysBefore)
            && today <= nextEarnings.Date.AddDays(strategy.EarningsBlockDaysAfter);

        if (earningsBlackout)
            warnings.Add("An earnings blackout window is active for this symbol.");
        if (orderedBars.Count < strategy.TrendSmaPeriod)
            warnings.Add("History depth is shorter than the configured trend SMA period.");

        var conditions = new List<SetupConditionDto>
        {
            new("trend", $"Price above {strategy.TrendSmaPeriod}-day SMA", trendMet,
                lastSma.HasValue ? $"Close {lastBar.Close:F2} vs SMA {lastSma.Value:F2}" : "Insufficient bars"),
            new("momentum", $"RSI({strategy.FastRsiPeriod}) below 35", momentumMet,
                lastRsi.HasValue ? $"RSI {lastRsi.Value:F2}" : "Insufficient bars"),
            new("trigger", "Setup trigger placeholder", setupTriggerMet,
                "Support / TD / Wyckoff trigger logic not implemented yet."),
            new("volume", $"Volume >= {strategy.VolumeConfirmationMultiplier}x 20-bar average", volumeMet,
                volumeAverage > 0m ? $"Volume {latestVolume} vs avg {volumeAverage:F0}" : "No volume data")
        };

        var metCount = conditions.Count(c => c.IsMet);
        var confidence = conditions.Count == 0
            ? 0m
            : Math.Round((decimal)metCount / conditions.Count, 4);
        var state = metCount >= strategy.MinimumConditionsToTrigger && !earningsBlackout
            ? "Triggered"
            : "Watching";

        var entryPrice = quote?.Price ?? lastBar.Close;
        var entryLower = Math.Round(entryPrice * (1m - 0.0025m), 4);
        var entryUpper = Math.Round(entryPrice * (1m + 0.0025m), 4);
        var stopLoss = Math.Round(entryPrice * (1m - strategy.StopLossPercent / 100m), 4);
        var takeProfit = Math.Round(entryPrice * (1m + strategy.TakeProfitPercent / 100m), 4);
        var risk = entryPrice - stopLoss;
        var reward = takeProfit - entryPrice;
        decimal? rr = null;
        if (risk > 0m)
            rr = Math.Round(reward / risk, 4);

        return new SingleStockAnalysisDto(
            symbol,
            request.Timeframe,
            quote?.Price ?? lastBar.Close,
            quote?.AsOf,
            orderedBars[0].Date,
            orderedBars[^1].Date,
            orderedBars.Count,
            lastSma,
            lastRsi,
            earningsBlackout,
            nextEarnings?.Date,
            new SetupSnapshotDto(
                "Long",
                state,
                entryPrice,
                entryLower,
                entryUpper,
                stopLoss,
                takeProfit,
                rr,
                confidence,
                conditions),
            strategy,
            warnings);
    }
}
