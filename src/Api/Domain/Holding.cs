namespace Stocky.Api.Domain;

public enum PositionStrategy
{
    General = 0,
    LongTerm = 1,
    Hodl = 2,
    MomentumPlays = 3
}

public class Holding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PortfolioId { get; set; }
    public Portfolio Portfolio { get; set; } = default!;
    public string Symbol { get; set; } = default!;
    public Instrument Instrument { get; set; } = default!;
    public decimal Quantity { get; set; }
    public decimal AverageCost { get; set; }
    public PositionStrategy Strategy { get; set; } = PositionStrategy.General;
    public decimal? Target1 { get; set; }
    public decimal? Target2 { get; set; }
    public decimal? Target3 { get; set; }
    public decimal? StopLoss { get; set; }
}
