using Stocky.Api.Dtos;

namespace Stocky.Api.Services;

/// <summary>
/// M8 "Data Providers & Real-Time" — extended market-data surface beyond
/// IMarketDataProvider's quote/news/earnings/bars. All methods are designed to
/// degrade gracefully: the stub implementation returns deterministic synthetic
/// data so the UI works offline, and real implementations can be slotted in
/// when API keys are configured.
/// </summary>
public interface IExtendedMarketDataProvider
{
    Task<OrderBookDto> GetOrderBookAsync(string symbol, int depth, CancellationToken ct = default);
    Task<ExtendedQuoteDto> GetExtendedQuoteAsync(string symbol, CancellationToken ct = default);
    Task<IReadOnlyList<FilingDto>> GetFilingsAsync(IReadOnlyCollection<string> symbols, int limit, CancellationToken ct = default);
    Task<IReadOnlyList<InsiderTradeDto>> GetInsiderTradesAsync(string symbol, int limit, CancellationToken ct = default);
    Task<ShortInterestDto> GetShortInterestAsync(string symbol, CancellationToken ct = default);
    Task<IReadOnlyList<EconomicEventDto>> GetEconomicCalendarAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<OptionsFlowDto> GetOptionsFlowAsync(string symbol, int limit, CancellationToken ct = default);
}
