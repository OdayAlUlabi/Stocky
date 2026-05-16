namespace Stocky.Api.Domain;

/// <summary>
/// Lot-selection method used when realising gains on a Sell transaction.
/// Drives <see cref="Stocky.Api.Services.TaxLotService"/>.
/// </summary>
public enum CostBasisMethod
{
    Fifo = 0,
    Lifo = 1,
    HighestCost = 2,
    LowestCost = 3,
}

public class Portfolio
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!; // Entra ID object id (oid claim)
    public string Name { get; set; } = default!;
    public string BaseCurrency { get; set; } = "USD";
    public CostBasisMethod CostBasisMethod { get; set; } = CostBasisMethod.Fifo;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>
    /// M9 #103. Optional benchmark ticker for performance / risk comparisons.
    /// Null means "use default" (SPY) at the service layer.
    /// </summary>
    public string? BenchmarkSymbol { get; set; }
    public List<Holding> Holdings { get; set; } = new();
    public List<Transaction> Transactions { get; set; } = new();
}
