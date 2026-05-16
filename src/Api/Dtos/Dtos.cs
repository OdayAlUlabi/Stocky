namespace Stocky.Api.Dtos;

public record PortfolioDto(Guid Id, string Name, string BaseCurrency, DateTimeOffset CreatedAt);
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
    DateTimeOffset AsOf);
