namespace Stocky.Api.Domain;

public class Holding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PortfolioId { get; set; }
    public Portfolio Portfolio { get; set; } = default!;
    public string Symbol { get; set; } = default!;
    public Instrument Instrument { get; set; } = default!;
    public decimal Quantity { get; set; }
    public decimal AverageCost { get; set; }
}
