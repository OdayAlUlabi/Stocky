namespace Stocky.Api.Dtos;

public record PortfolioDto(Guid Id, string Name, string BaseCurrency, DateTimeOffset CreatedAt, decimal CashBalance = 0m);
public record CreatePortfolioRequest(string Name, string BaseCurrency = "USD");
public record UpdatePortfolioRequest(string Name, string BaseCurrency);

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

// SCR-030 News, SCR-031 Earnings
public record NewsItemDto(long Id, string Headline, string? Summary, string Source, string? Url, string? Symbol, DateTimeOffset PublishedAt, string Category);
public record EarningsEventDto(long Id, string Symbol, DateOnly Date, string? Time, decimal? EpsEstimate, decimal? EpsActual, decimal? RevenueEstimate, decimal? RevenueActual);

// SCR-040 Settings
public record UserSettingsDto(string DisplayCurrency, string Theme, string Locale, bool EmailAlerts, bool WeeklyDigest);

// CSV import
public record ImportResultDto(int Imported, int Skipped, IReadOnlyList<string> Errors);
