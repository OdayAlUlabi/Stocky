namespace Stocky.Api.Dtos;

public record PortfolioDto(Guid Id, string Name, string BaseCurrency, DateTimeOffset CreatedAt, decimal CashBalance = 0m, string CostBasisMethod = "Fifo");
public record CreatePortfolioRequest(string Name, string BaseCurrency = "USD", string? CostBasisMethod = null);
public record UpdatePortfolioRequest(string Name, string BaseCurrency, string? CostBasisMethod = null);

public record HoldingDto(Guid Id, string Symbol, decimal Quantity, decimal AverageCost, decimal? LatestPrice, decimal? MarketValue);

public record TransactionDto(
    Guid Id,
    string? Symbol,
    string Type,
    decimal Quantity,
    decimal Price,
    decimal Fee,
    string Currency,
    DateTimeOffset ExecutedAt,
    string? Notes);

public record CreateTransactionRequest(
    string? Symbol,
    string Type,
    decimal Quantity,
    decimal Price,
    decimal Fee,
    string Currency,
    DateTimeOffset ExecutedAt,
    string? Notes);

public record ImportTransactionsRequest(string Csv);
public record ImportTransactionsRowError(int Row, string Message);
public record ImportTransactionsResult(int Imported, int Skipped, IReadOnlyList<ImportTransactionsRowError> Errors);

public record WatchlistDto(Guid Id, string Name, IReadOnlyList<WatchlistItemDto> Items);
public record WatchlistItemDto(Guid Id, string Symbol, decimal? LatestPrice, decimal? ChangePercent);
public record CreateWatchlistRequest(string Name);
public record AddWatchlistItemRequest(string Symbol);

public record QuoteDto(string Symbol, decimal Price, decimal? Change, decimal? ChangePercent, DateTimeOffset AsOf);

public record PortfolioPerformanceDto(
    Guid PortfolioId,
    decimal MarketValue,
    decimal CostBasis,
    decimal UnrealizedPnL,
    decimal UnrealizedPnLPercent,
    string Currency,
    DateTimeOffset AsOf,
    decimal CashBalance = 0m,
    decimal TotalEquity = 0m);

public record InstrumentDto(string Symbol, string Name, string Exchange, string Currency, string AssetClass);

// Stock screener: returns instrument + metadata + latest price for matches.
public record ScreenerRowDto(
    string Symbol,
    string Name,
    string AssetClass,
    string? Sector,
    string? Industry,
    string? Country,
    decimal? MarketCap,
    decimal? Beta,
    decimal? DividendYield,
    decimal? LatestPrice);

public record ScreenerResultDto(
    int Total,
    IReadOnlyList<ScreenerRowDto> Rows);

public record AllocationSliceDto(string Label, decimal Value, decimal Percent);

public record MoverDto(string Symbol, decimal MarketValue, decimal DayChangePercent);

public record ValuePointDto(DateTimeOffset Date, decimal Value);

public record DashboardDto(
    Guid? PortfolioId,
    string PortfolioName,
    string Currency,
    decimal TotalValue,
    decimal DayPnL,
    decimal DayPnLPercent,
    decimal TotalReturn,
    decimal TotalReturnPercent,
    IReadOnlyList<AllocationSliceDto> SectorAllocation,
    IReadOnlyList<AllocationSliceDto> AssetClassAllocation,
    IReadOnlyList<MoverDto> TopGainers,
    IReadOnlyList<MoverDto> TopLosers,
    IReadOnlyList<ValuePointDto> ValueHistory,
    DateTimeOffset AsOf,
    decimal CashBalance = 0m,
    decimal TotalEquity = 0m);

// SCR-005 Position detail
public record PositionDetailDto(
    string Symbol,
    string Name,
    string AssetClass,
    string? Sector,
    string Currency,
    decimal Quantity,
    decimal AverageCost,
    decimal? LatestPrice,
    decimal? MarketValue,
    decimal UnrealizedPnL,
    decimal UnrealizedPnLPercent,
    decimal RealizedPnL,
    decimal DividendsReceived,
    IReadOnlyList<TaxLotDto> Lots,
    IReadOnlyList<TransactionDto> Transactions,
    IReadOnlyList<ValuePointDto> PriceHistory);

public record TaxLotDto(
    Guid Id,
    DateTimeOffset OpenedAt,
    decimal Quantity,
    decimal RemainingQuantity,
    decimal CostPerShare,
    decimal CostBasis);

// SCR-008 Reports
public record ReportSummaryDto(
    decimal TotalDeposits,
    decimal TotalWithdrawals,
    decimal NetContributions,
    decimal MarketValue,
    decimal RealizedPnL,
    decimal UnrealizedPnL,
    decimal Dividends,
    decimal Fees,
    DateTimeOffset From,
    DateTimeOffset To,
    string Currency);

public record DividendRowDto(string Symbol, DateTimeOffset Date, decimal Amount, string Currency);

// SCR-009 Performance
public record PerformancePointDto(DateTimeOffset Date, decimal Value, decimal CostBasis, decimal TwrPercent);
public record PerformanceDto(
    Guid? PortfolioId,
    string Currency,
    decimal TwrPercent,
    decimal MwrPercent,
    decimal Best1Day,
    decimal Worst1Day,
    IReadOnlyList<PerformancePointDto> Series);

// SCR-010 Allocation
public record AllocationDto(
    IReadOnlyList<AllocationSliceDto> ByAsset,
    IReadOnlyList<AllocationSliceDto> BySector,
    IReadOnlyList<AllocationSliceDto> ByCurrency,
    IReadOnlyList<AllocationSliceDto> BySymbol,
    decimal TotalValue,
    string Currency);

// SCR-020 Alerts
public record AlertDto(
    Guid Id,
    string Symbol,
    string Condition,
    decimal Threshold,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? TriggeredAt,
    decimal? TriggeredValue,
    string? Note);

public record CreateAlertRequest(string Symbol, string Condition, decimal Threshold, string? Note);
public record UpdateAlertRequest(decimal Threshold, string Status, string? Note);

// SCR-021 Capital gains
public record RealizedGainDto(
    Guid Id,
    string Symbol,
    DateTimeOffset AcquiredAt,
    DateTimeOffset SoldAt,
    decimal Quantity,
    decimal CostBasis,
    decimal Proceeds,
    decimal Gain,
    bool IsLongTerm);

public record CapitalGainsDto(
    int Year,
    decimal ShortTermGain,
    decimal LongTermGain,
    decimal TotalGain,
    IReadOnlyList<RealizedGainDto> Lots);

// Wash-sale report (IRS §1091): disallowed losses where replacement shares were
// purchased within ±30 days of a realised loss.
public record WashSaleReplacementDto(Guid BuyTransactionId, DateTimeOffset BuyAt, decimal Shares);

public record WashSaleAdjustmentDto(
    Guid LotId,
    string Symbol,
    DateTimeOffset SoldAt,
    decimal LotQuantity,
    decimal LotLoss,
    decimal ReplacementShares,
    decimal DisallowedLoss,
    decimal AllowedLoss,
    IReadOnlyList<WashSaleReplacementDto> Replacements);

public record WashSaleReportDto(
    int Year,
    decimal TotalLoss,
    decimal DisallowedLoss,
    IReadOnlyList<WashSaleAdjustmentDto> Adjustments);

// SCR-030 News, SCR-031 Earnings
public record NewsItemDto(long Id, string Headline, string? Summary, string Source, string? Url, string? Symbol, DateTimeOffset PublishedAt, string Category);
public record EarningsEventDto(long Id, string Symbol, DateOnly Date, string? Time, decimal? EpsEstimate, decimal? EpsActual, decimal? RevenueEstimate, decimal? RevenueActual);

// SCR-040 Settings
public record UserSettingsDto(string DisplayCurrency, string Theme, string Locale, bool EmailAlerts, bool WeeklyDigest);

// CSV import
public record ImportResultDto(int Imported, int Skipped, IReadOnlyList<string> Errors);

// Daily OHLC close for historical bar fetches
public record DailyBarDto(DateOnly Date, decimal Close);

// SCR-041 Portfolio history since first transaction
public record PortfolioHistoryPointDto(
    DateOnly Date,
    decimal Cash,
    decimal MarketValue,
    decimal TotalEquity,
    decimal NetContributions);

public record PortfolioHistoryEventDto(
    DateOnly Date,
    string Type,
    string? Symbol,
    decimal Quantity,
    decimal Amount,
    string? Notes);

public record PortfolioHistoryDto(
    Guid PortfolioId,
    string Currency,
    DateOnly From,
    DateOnly To,
    decimal NetContributions,
    decimal TotalEquity,
    decimal TotalReturn,
    decimal TotalReturnPercent,
    IReadOnlyList<PortfolioHistoryPointDto> Series,
    IReadOnlyList<PortfolioHistoryEventDto> Events);

public record DrawdownPointDto(DateOnly Date, decimal DrawdownPercent);
public record DailyReturnPointDto(DateOnly Date, decimal ReturnPercent);
public record PortfolioAnalyticsDto(
    Guid PortfolioId,
    string Currency,
    DateOnly From,
    DateOnly To,
    decimal TotalReturnPercent,
    decimal Twrr,
    decimal TwrrAnnualised,
    decimal Mwrr,
    decimal Volatility,
    decimal Sharpe,
    decimal Beta,
    string BenchmarkSymbol,
    decimal MaxDrawdown,
    DateOnly MaxDrawdownDate,
    decimal PeakEquity,
    decimal BestDay,
    DateOnly BestDayDate,
    decimal WorstDay,
    DateOnly WorstDayDate,
    decimal TotalDividends,
    decimal TtmDividends,
    decimal DividendYield,
    IReadOnlyList<DrawdownPointDto> DrawdownSeries,
    IReadOnlyList<DailyReturnPointDto> DailyReturnSeries);

// Symbol correlation matrix over a daily-bar window
public record CorrelationDto(
    Guid PortfolioId,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<string> Symbols,
    IReadOnlyList<IReadOnlyList<decimal>> Matrix);

// Rebalance: per-symbol target weights and drift suggestions.
public record RebalanceTargetDto(string Symbol, decimal TargetWeightPercent);

public record RebalanceSuggestionDto(
    string Symbol,
    decimal CurrentValue,
    decimal CurrentWeightPercent,
    decimal TargetWeightPercent,
    decimal DriftPercent,
    decimal TradeValue, // positive = buy, negative = sell
    string Action);     // "Buy" | "Sell" | "Hold"

public record RebalanceReportDto(
    Guid PortfolioId,
    string Currency,
    decimal TotalValue,
    decimal TargetWeightSumPercent,
    IReadOnlyList<RebalanceSuggestionDto> Suggestions);


// SCR-080..M8 — Data Providers & Real-Time

// #2 Level 2 order book
public record OrderBookLevelDto(decimal Price, int Size);
public record OrderBookDto(string Symbol, IReadOnlyList<OrderBookLevelDto> Bids, IReadOnlyList<OrderBookLevelDto> Asks, DateTimeOffset AsOf);

// #102 After-hours / pre-market quotes
public record ExtendedQuoteDto(
    string Symbol,
    decimal RegularPrice,
    decimal ExtendedPrice,
    decimal ExtendedChange,
    decimal ExtendedChangePercent,
    string Session, // PreMarket | Regular | AfterHours | Closed
    DateTimeOffset AsOf);

// #4 SEC EDGAR filings
public record FilingDto(long Id, string Symbol, string Form, string Title, DateOnly FiledAt, string Url, string AccessionNumber);

// #5 Insider trades
public record InsiderTradeDto(long Id, string Symbol, string Insider, string Role, string Side, decimal Quantity, decimal Price, decimal Value, DateOnly FiledAt);

// #6 Short interest
public record ShortInterestPointDto(DateOnly ReportDate, decimal ShortInterest, decimal PercentOfFloat, decimal DaysToCover);
public record ShortInterestDto(
    string Symbol,
    DateOnly ReportDate,
    decimal ShortInterest,
    decimal FloatShares,
    decimal PercentOfFloat,
    decimal DaysToCover,
    IReadOnlyList<ShortInterestPointDto> History);

// #7 Economic calendar
public record EconomicEventDto(
    long Id,
    DateOnly Date,
    string Time,
    string Country,
    string Indicator,
    string Importance, // High | Medium | Low
    decimal? Actual,
    decimal? Forecast,
    decimal? Previous,
    string Unit);

// #8 Options flow
public record OptionsFlowRowDto(
    string Symbol,
    string Side, // Call | Put
    decimal Strike,
    DateOnly Expiry,
    int Volume,
    int OpenInterest,
    decimal VolumeOverOpenInterest,
    decimal Premium,
    decimal NotionalValue);
public record OptionsFlowDto(string Symbol, IReadOnlyList<OptionsFlowRowDto> Rows, DateTimeOffset AsOf);

// #1 Real-time price tick (SignalR push payload)
public record PriceTickDto(string Symbol, decimal Price, decimal? Change, decimal? ChangePercent, DateTimeOffset AsOf);
