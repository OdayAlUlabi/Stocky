namespace Stocky.Api.Domain;

/// <summary>
/// Daily portfolio value snapshot, written by SnapshotJob. Replaces SCR-002
/// price-quote-synthesised history once enough days have accumulated.
/// </summary>
public class PortfolioSnapshot
{
    public long Id { get; set; }
    public Guid PortfolioId { get; set; }
    public Portfolio Portfolio { get; set; } = default!;
    public DateOnly Date { get; set; }
    public decimal MarketValue { get; set; }
    public decimal CostBasis { get; set; }
    public decimal DayPnL { get; set; }
}
