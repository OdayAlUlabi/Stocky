using System.ComponentModel.DataAnnotations;

namespace Stocky.Api.Dtos;

public record PortfolioDto(Guid Id, string Name, string BaseCurrency, DateTimeOffset CreatedAt, decimal CashBalance = 0m, string CostBasisMethod = "Fifo");
public record CreatePortfolioRequest(
    [param: Required, StringLength(120, MinimumLength = 1)] string Name,
    [param: StringLength(8)] string BaseCurrency = "USD",
    string? CostBasisMethod = null);
public record UpdatePortfolioRequest(
    [param: Required, StringLength(120, MinimumLength = 1)] string Name,
    [param: Required, StringLength(8)] string BaseCurrency,
    string? CostBasisMethod = null);

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
    string? Notes,
    decimal? LatestPrice = null,
    decimal? MarketValue = null);

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

/// <summary>
/// Reference-data profile for a symbol as returned by an upstream broker /
/// market-data provider (Alpaca's <c>/v2/assets/{symbol}</c>). Populates the
/// long-lived columns on <c>Instrument</c>; refreshed on an admin trigger
/// rather than per-quote.
/// </summary>
public record AssetProfileDto(
    string Symbol,
    string? Name,
    string? Exchange,
    string? AssetClass,
    string? Status,
    bool? IsTradable,
    bool? IsFractionable,
    bool? IsShortable,
    bool? IsMarginable,
    bool? IsEasyToBorrow,
    decimal? MaintenanceMarginRequirement);

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

public record MoverDto(string Symbol, decimal MarketValue, decimal DayChangePercent, decimal DayChange = 0m);

public record ValuePointDto(DateTimeOffset Date, decimal Value, decimal Cash = 0m, decimal MarketValue = 0m);

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
    IReadOnlyList<ValuePointDto> PriceHistory,
    // Extended market data (optional — null when market data provider not wired)
    decimal? Change = null,
    decimal? ChangePercent = null,
    string? Industry = null,
    string? Country = null,
    string? Exchange = null,
    decimal? MarketCap = null,
    decimal? Beta = null,
    decimal? DividendYield = null);

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
    string? Note,
    // M10 additions
    string Type = "Price",
    int? IndicatorPeriod = null,
    string? KeywordFilter = null,
    decimal? MinSentiment = null,
    int? DaysBeforeEarnings = null,
    Guid? PortfolioId = null,
    string Channels = "Inbox",
    string? WebhookUrl = null,
    DateTimeOffset? SnoozedUntil = null);

public record CreateAlertRequest(
    string Symbol,
    string Condition,
    decimal Threshold,
    string? Note,
    string? Type = null,
    int? IndicatorPeriod = null,
    string? KeywordFilter = null,
    decimal? MinSentiment = null,
    int? DaysBeforeEarnings = null,
    Guid? PortfolioId = null,
    string? Channels = null,
    string? WebhookUrl = null);

public record UpdateAlertRequest(
    decimal Threshold,
    string Status,
    string? Note,
    string? Channels = null,
    string? WebhookUrl = null,
    int? IndicatorPeriod = null,
    string? KeywordFilter = null,
    decimal? MinSentiment = null,
    int? DaysBeforeEarnings = null);

// M10 #53 Alert history
public record AlertEventDto(
    Guid Id,
    Guid AlertId,
    string Symbol,
    string Type,
    string Condition,
    DateTimeOffset TriggeredAt,
    decimal? TriggeredValue,
    string Message,
    string Channels,
    string? Context);

public record SnoozeAlertRequest(DateTimeOffset UntilUtc);

// M10 #51 insider trades (alert side — separate from M8 InsiderTradeDto from data feed)
public record InsiderEventDto(
    Guid Id,
    string Symbol,
    string InsiderName,
    string Relation,
    string TransactionType,
    decimal Shares,
    decimal Price,
    DateTimeOffset FiledAt);

public record InsiderClusterDto(
    string Symbol,
    int BuyCount,
    int SellCount,
    decimal NetShares,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    IReadOnlyList<InsiderEventDto> Trades);

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
public record DailyBarDto(DateOnly Date, decimal Close, decimal? Open = null, decimal? High = null, decimal? Low = null, long? Volume = null);

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

// ─────────────────────────────────────────────────────────────────────────
// M9 — Advanced Analytics & Charts
// ─────────────────────────────────────────────────────────────────────────

// #21 OHLCV bars for charting (TradingView Lightweight Charts)
public record OhlcBarDto(DateOnly Date, decimal Open, decimal High, decimal Low, decimal Close, long Volume);

// #22 Analyst ratings
public record AnalystRatingDistributionDto(int StrongBuy, int Buy, int Hold, int Sell, int StrongSell);
public record AnalystRatingDto(
    string Symbol,
    decimal ConsensusScore,
    string ConsensusLabel,
    decimal PriceTargetLow,
    decimal PriceTargetMean,
    decimal PriceTargetHigh,
    decimal PriceTargetMedian,
    int AnalystCount,
    DateOnly AsOf,
    AnalystRatingDistributionDto Distribution);

// #23 Extended risk metrics
public record RiskMetricsDto(
    Guid PortfolioId,
    DateOnly From,
    DateOnly To,
    decimal Sharpe,
    decimal Sortino,
    decimal MaxDrawdown,
    DateOnly MaxDrawdownDate,
    decimal Var95,
    decimal Var99,
    decimal Cvar95,
    decimal AnnualisedVolatility,
    decimal DownsideVolatility,
    decimal Beta,
    decimal Alpha,
    string BenchmarkSymbol);

// #24 Backtesting engine
public record BacktestRequest(
    Guid PortfolioId,
    DateOnly From,
    DateOnly To,
    decimal InitialCash,
    decimal MonthlyContribution,
    string Frequency, // Monthly | Quarterly | Yearly
    IReadOnlyList<RebalanceTargetDto> Targets);

public record BacktestPointDto(DateOnly Date, decimal Equity, decimal Contributions, decimal BenchmarkEquity);
public record BacktestDto(
    Guid PortfolioId,
    string BenchmarkSymbol,
    decimal FinalEquity,
    decimal TotalContributions,
    decimal TotalReturnPercent,
    decimal Cagr,
    decimal MaxDrawdown,
    decimal BenchmarkFinalEquity,
    decimal BenchmarkTotalReturnPercent,
    decimal BenchmarkCagr,
    IReadOnlyList<BacktestPointDto> Series);

// #95 Earnings calendar surprise history
public record EarningsSurprisePointDto(DateOnly Date, decimal? EpsEstimate, decimal? EpsActual, decimal? SurprisePercent);

// #103 Benchmark comparison
public record BenchmarkComponentDto(string Symbol, decimal Weight);
public record BenchmarkConfigDto(string? Symbol, IReadOnlyList<BenchmarkComponentDto>? Blend);
public record BenchmarkPointDto(DateOnly Date, decimal PortfolioEquity, decimal BenchmarkEquity, decimal OutperformanceBps);
public record BenchmarkComparisonDto(
    Guid PortfolioId,
    string BenchmarkLabel,
    DateOnly From,
    DateOnly To,
    decimal PortfolioReturnPercent,
    decimal BenchmarkReturnPercent,
    decimal OutperformanceBps,
    decimal Alpha,
    decimal Beta,
    IReadOnlyList<BenchmarkPointDto> Series);

// #104 Goals & target NAV tracking
public record GoalCreateDto(
    Guid? PortfolioId,
    string Name,
    decimal TargetValue,
    DateOnly TargetDate,
    decimal MonthlyContribution,
    decimal ExpectedReturn);

public record GoalProjectionPointDto(DateOnly Date, decimal ProjectedValue, decimal TargetTrajectory);
public record GoalDto(
    Guid Id,
    Guid? PortfolioId,
    string Name,
    decimal TargetValue,
    DateOnly TargetDate,
    decimal MonthlyContribution,
    decimal ExpectedReturn,
    decimal CurrentValue,
    decimal ProgressPercent,
    DateOnly? ProjectedHitDate,
    bool OnTrack,
    decimal ProjectedFinalValue,
    IReadOnlyList<GoalProjectionPointDto> Projection);

// ---------------- M11 Reporting & Sharing ----------------

// #54 sharing
public record ShareTokenDto(
    Guid Id,
    // Plaintext token + share URL are returned ONLY at creation time.
    // Subsequent reads (List) leave these null; clients should treat the
    // creation response as the single opportunity to copy the link.
    string? Token,
    string TokenPrefix,
    Guid PortfolioId,
    string? Label,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    int ViewCount,
    DateTimeOffset? LastViewedAt,
    bool IncludeTransactions,
    bool IncludeCostBasis,
    bool IsActive,
    string? ShareUrl);

public record CreateShareTokenRequest(
    Guid PortfolioId,
    string? Label,
    DateTimeOffset? ExpiresAt,
    bool IncludeTransactions = false,
    bool IncludeCostBasis = false);

public record SharedHoldingRowDto(
    string Symbol,
    decimal Quantity,
    decimal? LatestPrice,
    decimal? MarketValue,
    decimal? AverageCost,
    decimal? UnrealizedPnL);

public record SharedTransactionRowDto(
    DateTimeOffset ExecutedAt,
    string Type,
    string? Symbol,
    decimal Quantity,
    decimal Price);

public record SharedPortfolioDto(
    string PortfolioName,
    string BaseCurrency,
    DateTimeOffset GeneratedAt,
    decimal TotalMarketValue,
    decimal? TotalUnrealizedPnL,
    bool IncludesCostBasis,
    bool IncludesTransactions,
    IReadOnlyList<SharedHoldingRowDto> Holdings,
    IReadOnlyList<SharedTransactionRowDto>? Transactions);

// #55 scheduled exports
public record ReportScheduleDto(
    Guid Id,
    Guid PortfolioId,
    string Type,
    string Format,
    string Cadence,
    string? Email,
    bool Enabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset NextRunUtc,
    DateTimeOffset? LastRunUtc);

public record CreateReportScheduleRequest(
    Guid PortfolioId,
    string Type,
    string Format,
    string Cadence,
    string? Email,
    bool Enabled = true);

public record UpdateReportScheduleRequest(
    string? Type,
    string? Format,
    string? Cadence,
    string? Email,
    bool? Enabled);

public record ReportDeliveryDto(
    Guid Id,
    Guid? ScheduleId,
    Guid PortfolioId,
    string Type,
    string Format,
    string FileName,
    string ContentType,
    int SizeBytes,
    DateTimeOffset GeneratedAt,
    string? Trigger,
    string? Channel);

// === M14 Platform & Admin ===

public record CashTransactionDto(
    Guid Id,
    Guid PortfolioId,
    string Type,                 // Deposit | Withdrawal | Fee | Dividend | Interest
    decimal Amount,              // signed: deposits/dividends/interest positive; withdrawals/fees negative
    string Currency,
    DateTimeOffset ExecutedAt,
    string? Notes);

public record CreateCashTransactionRequest(
    Guid PortfolioId,
    string Type,
    decimal Amount,
    string Currency = "USD",
    DateTimeOffset? ExecutedAt = null,
    string? Notes = null);

public record CashBalanceDto(Guid PortfolioId, string Currency, decimal Balance, int Count);

public record PositionNoteDto(Guid Id, Guid? PortfolioId, string Symbol, string Body, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public record CreatePositionNoteRequest(string Symbol, string Body, Guid? PortfolioId = null);
public record UpdatePositionNoteRequest(string Body);

public record AuditEntryDto(
    Guid Id,
    DateTimeOffset Timestamp,
    string Action,
    string Resource,
    string? ResourceId,
    string? Method,
    string? Path,
    int? StatusCode,
    string? ClientIp,
    string? Details);

public record ModelTemplateAllocationDto(string Symbol, string Name, string AssetClass, decimal WeightPercent);
public record ModelPortfolioTemplateDto(string Slug, string Name, string Description, string Risk, IReadOnlyList<ModelTemplateAllocationDto> Allocations);

public record ApplyTemplateRequest(string Slug, string PortfolioName, string BaseCurrency = "USD", decimal? InitialCashDeposit = null);

public record GdprExportDto(
    string OwnerId,
    DateTimeOffset GeneratedAt,
    object Portfolios,
    object Holdings,
    object Transactions,
    object Watchlists,
    object Alerts,
    object PositionNotes,
    object Goals,
    object UserSettings);

// M14 #91 — API key DTOs
public record ApiKeyDto(
    Guid Id,
    string Name,
    string Prefix,
    string Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastUsedAt,
    bool IsActive);

public record CreateApiKeyRequest(string Name, string? Scopes, DateTimeOffset? ExpiresAt);
public record CreatedApiKeyDto(ApiKeyDto Key, string Plaintext);
