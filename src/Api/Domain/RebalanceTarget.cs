namespace Stocky.Api.Domain;

/// <summary>
/// Per-symbol target weight (in percent of total portfolio value) used by
/// the rebalance suggestion engine. Totals across a portfolio do not need
/// to sum to exactly 100 — anything left over is treated as a cash target.
/// </summary>
public class RebalanceTarget
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PortfolioId { get; set; }
    public Portfolio Portfolio { get; set; } = default!;
    public string Symbol { get; set; } = default!;
    public decimal TargetWeightPercent { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
