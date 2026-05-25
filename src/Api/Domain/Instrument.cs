namespace Stocky.Api.Domain;

public class Instrument
{
    public string Symbol { get; set; } = default!; // primary key
    public string Name { get; set; } = default!;
    public string Exchange { get; set; } = default!;
    public string Currency { get; set; } = "USD";
    public string AssetClass { get; set; } = "Equity"; // Equity | ETF | Crypto | Cash

    // ---- Asset profile (sourced from Alpaca /v2/assets/{symbol}) ----
    // Populated by DataRefreshService.EnrichInstrumentsOnceAsync. All
    // nullable so legacy / placeholder rows ("Exchange=UNKNOWN") keep working
    // until the next enrichment pass fills them in.

    /// <summary>"active" | "inactive" per Alpaca.</summary>
    public string? Status { get; set; }

    /// <summary>Alpaca says the symbol is tradable on their platform.</summary>
    public bool? IsTradable { get; set; }

    /// <summary>Supports fractional-share orders (e.g. AAPL: yes; BRK.A: no).</summary>
    public bool? IsFractionable { get; set; }

    /// <summary>Eligible to be shorted via Alpaca.</summary>
    public bool? IsShortable { get; set; }

    /// <summary>Marginable on the Alpaca platform.</summary>
    public bool? IsMarginable { get; set; }

    /// <summary>Easy-to-borrow list eligibility (impacts short locate cost).</summary>
    public bool? IsEasyToBorrow { get; set; }

    /// <summary>Maintenance-margin requirement Alpaca reports for this symbol (percent).</summary>
    public decimal? MaintenanceMarginRequirement { get; set; }

    /// <summary>Last time the asset profile fields above were refreshed.</summary>
    public DateTimeOffset? ProfileUpdatedAt { get; set; }
}
