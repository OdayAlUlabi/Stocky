namespace Stocky.Api.Domain;

public class Instrument
{
    public string Symbol { get; set; } = default!; // primary key
    public string Name { get; set; } = default!;
    public string Exchange { get; set; } = default!;
    public string Currency { get; set; } = "USD";
    public string AssetClass { get; set; } = "Equity"; // Equity | ETF | Crypto | Cash
}
