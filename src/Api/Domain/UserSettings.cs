namespace Stocky.Api.Domain;

public class UserSettings
{
    public string OwnerId { get; set; } = default!; // PK
    public string DisplayCurrency { get; set; } = "USD";
    public string Theme { get; set; } = "auto"; // light | dark | auto
    public string Locale { get; set; } = "en-US";
    public bool EmailAlerts { get; set; } = true;
    public bool WeeklyDigest { get; set; } = false;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
