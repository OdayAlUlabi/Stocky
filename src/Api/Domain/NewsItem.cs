namespace Stocky.Api.Domain;

public class NewsItem
{
    public long Id { get; set; }
    public string Headline { get; set; } = default!;
    public string? Summary { get; set; }
    public string Source { get; set; } = default!;
    public string? Url { get; set; }
    public string? Symbol { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public string Category { get; set; } = "General"; // General | Earnings | Macro | Symbol
}

public class EarningsEvent
{
    public long Id { get; set; }
    public string Symbol { get; set; } = default!;
    public DateOnly Date { get; set; }
    public string? Time { get; set; } // BMO | AMC | TBD
    public decimal? EpsEstimate { get; set; }
    public decimal? EpsActual { get; set; }
    public decimal? RevenueEstimate { get; set; }
    public decimal? RevenueActual { get; set; }
}
