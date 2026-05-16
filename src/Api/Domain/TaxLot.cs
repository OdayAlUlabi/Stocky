namespace Stocky.Api.Domain;

/// <summary>
/// FIFO open tax lot. Created from Buy transactions; consumed by Sells.
/// Recomputed deterministically whenever transactions change.
/// </summary>
public class TaxLot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PortfolioId { get; set; }
    public Portfolio Portfolio { get; set; } = default!;
    public string Symbol { get; set; } = default!;
    public Guid OpenedByTransactionId { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public decimal Quantity { get; set; }
    public decimal RemainingQuantity { get; set; }
    public decimal CostPerShare { get; set; }
}

/// <summary>
/// Realised gain produced when a sell consumes one or more open lots.
/// </summary>
public class RealizedGain
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PortfolioId { get; set; }
    public Portfolio Portfolio { get; set; } = default!;
    public string Symbol { get; set; } = default!;
    public Guid SellTransactionId { get; set; }
    public Guid LotId { get; set; }
    public DateTimeOffset AcquiredAt { get; set; }
    public DateTimeOffset SoldAt { get; set; }
    public decimal Quantity { get; set; }
    public decimal CostBasis { get; set; }
    public decimal Proceeds { get; set; }
    public decimal Gain { get; set; }
    public bool IsLongTerm { get; set; }
}
