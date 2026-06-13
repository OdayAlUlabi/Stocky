using Stocky.Api.Dtos;
using Stocky.Api.Services;
using Xunit;

namespace Stocky.Api.Tests;

public class SingleStockAnalysisServiceTests
{
    [Fact]
    public async Task BuildAsync_returns_daily_snapshot_with_conditions_and_risk_levels()
    {
        var service = new SingleStockAnalysisService(
            new BarsStubMarketDataProvider(),
            new TechnicalIndicatorService());

        var result = await service.BuildAsync(new SingleStockAnalysisRequest("AAPL"));

        Assert.Equal("AAPL", result.Symbol);
        Assert.Equal("1D", result.Timeframe);
        Assert.True(result.BarCount > 0);
        Assert.NotNull(result.CurrentPrice);
        Assert.Equal(4, result.Setup.Conditions.Count);
        Assert.Equal("Long", result.Setup.Direction);
        Assert.True(result.Setup.EntryUpperBound >= result.Setup.EntryLowerBound);
        Assert.True(result.Setup.TakeProfitPrice > result.Setup.EntryPrice);
        Assert.True(result.Setup.StopLossPrice < result.Setup.EntryPrice);
    }

    [Fact]
    public async Task BuildAsync_returns_warning_when_symbol_has_no_bars()
    {
        var service = new SingleStockAnalysisService(
            new EmptyMarketDataProvider(),
            new TechnicalIndicatorService());

        var result = await service.BuildAsync(new SingleStockAnalysisRequest("MISSING"));

        Assert.Equal(0, result.BarCount);
        Assert.Contains(result.Warnings, warning => warning.Contains("No historical bars", StringComparison.Ordinal));
        Assert.Empty(result.Setup.Conditions);
    }

    private sealed class EmptyMarketDataProvider : IMarketDataProvider
    {
        public Task<IReadOnlyList<AssetProfileDto>> GetAssetProfilesAsync(IReadOnlyCollection<string> symbols, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AssetProfileDto>>(Array.Empty<AssetProfileDto>());

        public Task<IReadOnlyDictionary<string, IReadOnlyList<DailyBarDto>>> GetDailyBarsAsync(
            IReadOnlyCollection<string> symbols, DateOnly from, DateOnly to, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<DailyBarDto>>>(
                new Dictionary<string, IReadOnlyList<DailyBarDto>>(StringComparer.OrdinalIgnoreCase));

        public Task<IReadOnlyList<EarningsEventDto>> GetEarningsAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EarningsEventDto>>(Array.Empty<EarningsEventDto>());

        public Task<IReadOnlyList<NewsItemDto>> GetNewsAsync(IReadOnlyCollection<string>? symbols, int limit, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<NewsItemDto>>(Array.Empty<NewsItemDto>());

        public Task<IReadOnlyList<QuoteDto>> GetQuotesAsync(IReadOnlyCollection<string> symbols, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<QuoteDto>>(Array.Empty<QuoteDto>());
    }

    private sealed class BarsStubMarketDataProvider : IMarketDataProvider
    {
        public Task<IReadOnlyList<AssetProfileDto>> GetAssetProfilesAsync(IReadOnlyCollection<string> symbols, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AssetProfileDto>>(Array.Empty<AssetProfileDto>());

        public Task<IReadOnlyDictionary<string, IReadOnlyList<DailyBarDto>>> GetDailyBarsAsync(
            IReadOnlyCollection<string> symbols, DateOnly from, DateOnly to, CancellationToken ct = default)
        {
            var dict = new Dictionary<string, IReadOnlyList<DailyBarDto>>(StringComparer.OrdinalIgnoreCase);
            foreach (var sym in symbols)
            {
                var bars = new List<DailyBarDto>();
                var rng = new Random(42);
                var price = 150m;
                var date = to.AddDays(-100);
                while (date <= to)
                {
                    price = Math.Max(1m, price + (decimal)(rng.NextDouble() * 4 - 2));
                    bars.Add(new DailyBarDto(date, price, price * 1.01m, price * 1.02m, price * 0.99m, 1_000_000));
                    date = date.AddDays(1);
                }
                dict[sym] = bars;
            }
            return Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<DailyBarDto>>>(dict);
        }

        public Task<IReadOnlyList<EarningsEventDto>> GetEarningsAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EarningsEventDto>>(Array.Empty<EarningsEventDto>());

        public Task<IReadOnlyList<NewsItemDto>> GetNewsAsync(IReadOnlyCollection<string>? symbols, int limit, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<NewsItemDto>>(Array.Empty<NewsItemDto>());

        public Task<IReadOnlyList<QuoteDto>> GetQuotesAsync(IReadOnlyCollection<string> symbols, CancellationToken ct = default)
        {
            var quotes = symbols.Select(s => new QuoteDto(s.ToUpperInvariant(), 150m, 1m, 0.67m, DateTimeOffset.UtcNow)).ToList();
            return Task.FromResult<IReadOnlyList<QuoteDto>>(quotes);
        }
    }
}
