namespace Stocky.Api.Domain;

public enum AlertCondition
{
    PriceAbove,
    PriceBelow,
    DayChangePercentAbove,
    DayChangePercentBelow
}

public enum AlertStatus
{
    Active,
    Triggered,
    Disabled
}

public class Alert
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!;
    public string Symbol { get; set; } = default!;
    public AlertCondition Condition { get; set; }
    public decimal Threshold { get; set; }
    public AlertStatus Status { get; set; } = AlertStatus.Active;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? TriggeredAt { get; set; }
    public decimal? TriggeredValue { get; set; }
    public string? Note { get; set; }
}
