namespace Stocky.Api.Domain;

/// <summary>
/// One daily OHLC bar per (Symbol, Date). Backfilled by
/// <c>HistoricalDataBackfillJob</c> for every symbol that appears in any
/// <see cref="Transaction"/>, starting at the earliest purchase date for
/// that symbol. Independent of the high-frequency intraday <see cref="PriceQuote"/>
/// stream: this table is the canonical long-term price history used by
/// analytics, charting, and back-testing.
/// </summary>
public class HistoricalPrice
{
    public long Id { get; set; }
    public string Symbol { get; set; } = default!;
    public DateOnly Date { get; set; }
    public decimal Close { get; set; }
    public decimal? Open { get; set; }
    public decimal? High { get; set; }
    public decimal? Low { get; set; }
    public long? Volume { get; set; }
    public string Source { get; set; } = "provider";
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
}
