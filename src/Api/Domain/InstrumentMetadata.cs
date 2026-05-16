namespace Stocky.Api.Domain;

/// <summary>
/// Extra columns on Instrument we keep stable for now; once a real
/// reference data provider lands these will be populated nightly.
/// Sector & Industry feed SCR-010 (Allocation) and SCR-002 sector pie.
/// </summary>
public class InstrumentMetadata
{
    public string Symbol { get; set; } = default!; // PK = Instrument.Symbol
    public Instrument Instrument { get; set; } = default!;
    public string? Sector { get; set; }
    public string? Industry { get; set; }
    public string? Country { get; set; }
    public decimal? MarketCap { get; set; }
    public decimal? Beta { get; set; }
    public decimal? DividendYield { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
