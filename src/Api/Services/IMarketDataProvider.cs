using Stocky.Api.Dtos;

namespace Stocky.Api.Services;

/// <summary>
/// Pluggable market data provider. The default StubMarketDataProvider returns
/// deterministic synthetic quotes so the app is functional without an API key;
/// AlpacaMarketDataProvider is auto-selected when Alpaca credentials are set.
/// </summary>
public interface IMarketDataProvider
{
    Task<IReadOnlyList<QuoteDto>> GetQuotesAsync(IReadOnlyCollection<string> symbols, CancellationToken ct = default);
    Task<IReadOnlyList<NewsItemDto>> GetNewsAsync(IReadOnlyCollection<string>? symbols, int limit, CancellationToken ct = default);
    Task<IReadOnlyList<EarningsEventDto>> GetEarningsAsync(DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>
    /// Daily OHLC close bars per symbol between <paramref name="from"/> and
    /// <paramref name="to"/> (both inclusive). Returns split-adjusted closes
    /// when the provider supports it. Symbols the provider cannot resolve
    /// simply yield an empty list — callers should carry forward the last
    /// known price.
    /// </summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<DailyBarDto>>> GetDailyBarsAsync(
        IReadOnlyCollection<string> symbols, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>
    /// Reference-data profile (name, exchange, asset class, tradable / shortable /
    /// fractionable flags, margin requirement) for each requested symbol.
    /// Symbols the provider cannot resolve are simply omitted from the result —
    /// callers should treat that as "no update".
    /// </summary>
    Task<IReadOnlyList<AssetProfileDto>> GetAssetProfilesAsync(
        IReadOnlyCollection<string> symbols, CancellationToken ct = default);
}
