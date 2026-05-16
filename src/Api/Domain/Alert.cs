namespace Stocky.Api.Domain;

public enum AlertCondition
{
    PriceAbove,
    PriceBelow,
    DayChangePercentAbove,
    DayChangePercentBelow,
    // M10 #47 technical indicators
    SmaCrossAbove,
    SmaCrossBelow,
    RsiAbove,
    RsiBelow,
    // M10 #48/#49/#50/#51 — events (threshold semantics vary by type)
    EarningsWithinDays,
    NewsKeyword,
    DriftAbovePercent,
    InsiderClusterBuy,
    InsiderClusterSell
}

public enum AlertStatus
{
    Active,
    Triggered,
    Disabled
}

/// <summary>
/// M10 — broader alert taxonomy. Drives which evaluator picks up the row.
/// </summary>
public enum AlertType
{
    Price = 0,
    Technical = 1,
    Earnings = 2,
    News = 3,
    Drift = 4,
    Insider = 5
}

public class Alert
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!;
    /// <summary>Symbol the alert applies to. Optional for portfolio-scoped alerts (e.g. drift).</summary>
    public string Symbol { get; set; } = default!;
    public AlertCondition Condition { get; set; }
    public decimal Threshold { get; set; }
    public AlertStatus Status { get; set; } = AlertStatus.Active;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? TriggeredAt { get; set; }
    public decimal? TriggeredValue { get; set; }
    public string? Note { get; set; }

    // M10 additions ----------------------------------------------------------
    public AlertType Type { get; set; } = AlertType.Price;
    /// <summary>Lookback period for SMA/RSI (days). Null for non-technical alerts.</summary>
    public int? IndicatorPeriod { get; set; }
    /// <summary>Substring filter for #49 news alerts (case-insensitive).</summary>
    public string? KeywordFilter { get; set; }
    /// <summary>Minimum (signed) sentiment required to fire a news alert. Range −1..+1.</summary>
    public decimal? MinSentiment { get; set; }
    /// <summary>For #48 earnings alerts: how many days ahead of report date to fire.</summary>
    public int? DaysBeforeEarnings { get; set; }
    /// <summary>Optional portfolio scoping for drift / earnings / news alerts.</summary>
    public Guid? PortfolioId { get; set; }
    /// <summary>CSV of delivery channels: e.g. "Inbox,Email,Webhook".</summary>
    public string Channels { get; set; } = "Inbox";
    /// <summary>Webhook target for #52 Webhook channel.</summary>
    public string? WebhookUrl { get; set; }
    /// <summary>If set and > now, the alert is hidden / suppressed until this instant.</summary>
    public DateTimeOffset? SnoozedUntil { get; set; }
}

/// <summary>
/// M10 #53 — persistent history row written each time an alert fires.
/// Survives "Reactivate" so the user can see prior trips.
/// </summary>
public class AlertEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AlertId { get; set; }
    public Alert? Alert { get; set; }
    public string OwnerId { get; set; } = default!;
    public string Symbol { get; set; } = default!;
    public AlertType Type { get; set; }
    public AlertCondition Condition { get; set; }
    public DateTimeOffset TriggeredAt { get; set; } = DateTimeOffset.UtcNow;
    public decimal? TriggeredValue { get; set; }
    /// <summary>Human-readable summary (e.g. "AAPL crossed above 200d SMA at 195.20").</summary>
    public string Message { get; set; } = default!;
    /// <summary>CSV of channels the dispatcher delivered to.</summary>
    public string Channels { get; set; } = "Inbox";
    /// <summary>Optional reference (news id, earnings date, transaction id).</summary>
    public string? Context { get; set; }
}

/// <summary>
/// M10 #51 — insider-trade row used by the cluster detector. Real provider
/// (Finnhub / Quiver / SEC) would populate this; the stub seeds deterministic
/// rows so tests + dev UI light up.
/// </summary>
public class InsiderTrade
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Symbol { get; set; } = default!;
    public string InsiderName { get; set; } = default!;
    public string Relation { get; set; } = "Officer"; // Director, 10% Owner, etc.
    /// <summary>"Buy" | "Sell".</summary>
    public string TransactionType { get; set; } = "Buy";
    public decimal Shares { get; set; }
    public decimal Price { get; set; }
    public DateTimeOffset FiledAt { get; set; } = DateTimeOffset.UtcNow;
}
