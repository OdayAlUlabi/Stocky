using Stocky.Api.Dtos;
using Stocky.Api.Services;
using Xunit;

namespace Stocky.Api.Tests;

public class SingleStockTradingBotServiceTests
{
    [Fact]
    public async Task RunBacktestAsync_returns_equity_curve_and_metrics()
    {
        var service = new SingleStockTradingBotService(new DeterministicMarketDataProvider(), new TechnicalIndicatorService());

        var result = await service.RunBacktestAsync(new SingleStockBacktestRequest("AAPL"));

        Assert.Equal("AAPL", result.Symbol);
        Assert.Equal("1D", result.Timeframe);
        Assert.NotEmpty(result.EquityCurve);
        Assert.True(result.InitialCash > 0m);
        Assert.NotNull(result.Verdict);
        Assert.NotNull(result.TradeLog);
    }

    [Fact]
    public async Task RunWalkForwardAsync_returns_two_split_results()
    {
        var service = new SingleStockTradingBotService(new DeterministicMarketDataProvider(), new TechnicalIndicatorService());

        var result = await service.RunWalkForwardAsync(new SingleStockWalkForwardRequest("AAPL"));

        Assert.Equal("AAPL", result.Symbol);
        Assert.NotNull(result.InSample);
        Assert.NotNull(result.OutOfSample);
        Assert.NotEmpty(result.Verdict);
    }

    private sealed class DeterministicMarketDataProvider : IMarketDataProvider
    {
        public Task<IReadOnlyList<AssetProfileDto>> GetAssetProfilesAsync(IReadOnlyCollection<string> symbols, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AssetProfileDto>>(Array.Empty<AssetProfileDto>());

        public Task<IReadOnlyDictionary<string, IReadOnlyList<DailyBarDto>>> GetDailyBarsAsync(IReadOnlyCollection<string> symbols, DateOnly from, DateOnly to, CancellationToken ct = default)
        {
            var result = new Dictionary<string, IReadOnlyList<DailyBarDto>>(StringComparer.OrdinalIgnoreCase)
            {
                ["AAPL"] = BuildBars(from, to, 120m),
                ["SPY"] = BuildBars(from, to, 400m)
            };
            return Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<DailyBarDto>>>(result);
        }

        public Task<IReadOnlyList<EarningsEventDto>> GetEarningsAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EarningsEventDto>>(Array.Empty<EarningsEventDto>());

        public Task<IReadOnlyList<NewsItemDto>> GetNewsAsync(IReadOnlyCollection<string>? symbols, int limit, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<NewsItemDto>>(Array.Empty<NewsItemDto>());

        public Task<IReadOnlyList<QuoteDto>> GetQuotesAsync(IReadOnlyCollection<string> symbols, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<QuoteDto>>(Array.Empty<QuoteDto>());

        private static IReadOnlyList<DailyBarDto> BuildBars(DateOnly from, DateOnly to, decimal start)
        {
            var bars = new List<DailyBarDto>();
            var price = start;
            for (var day = from; day <= to; day = day.AddDays(1))
            {
                if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                    continue;

                var offset = bars.Count % 30;
                var drift = offset < 15 ? 1.2m : -0.9m;
                price = Math.Max(5m, price + drift);
                var open = price - 0.4m;
                var close = price;
                var high = price + 0.8m;
                var low = price - 1.0m;
                var volume = 100_000L + (bars.Count % 7) * 20_000L;
                bars.Add(new DailyBarDto(day, close, open, high, low, volume));
            }
            return bars;
        }
    }
}