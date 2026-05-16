using Stocky.Api.Dtos;

namespace Stocky.Api.Services;

/// <summary>
/// Pluggable market data provider. The default StubMarketDataProvider returns
/// deterministic synthetic quotes so the app is functional without an API key;
/// a Finnhub/FMP implementation can drop in by switching the DI registration.
/// </summary>
public interface IMarketDataProvider
{
    Task<IReadOnlyList<QuoteDto>> GetQuotesAsync(IReadOnlyCollection<string> symbols, CancellationToken ct = default);
    Task<IReadOnlyList<NewsItemDto>> GetNewsAsync(IReadOnlyCollection<string>? symbols, int limit, CancellationToken ct = default);
    Task<IReadOnlyList<EarningsEventDto>> GetEarningsAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}
