namespace Stocky.Api.Domain;

public class PriceQuote
{
    public long Id { get; set; }
    public string Symbol { get; set; } = default!;
    public Instrument Instrument { get; set; } = default!;
    public decimal Price { get; set; }
    public decimal? Change { get; set; }
    public decimal? ChangePercent { get; set; }
    public DateTimeOffset AsOf { get; set; }
}
