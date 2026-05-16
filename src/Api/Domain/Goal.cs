namespace Stocky.Api.Domain;

/// <summary>
/// M9 #104. A financial goal (e.g. "$1M by 2040") owned by a user and
/// optionally tied to a portfolio so its current value tracks that portfolio's
/// equity. ExpectedReturn is annualised (0.07 = 7%/yr) and feeds the
/// projection used by the goals endpoint.
/// </summary>
public class Goal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!;
    public Guid? PortfolioId { get; set; }
    public string Name { get; set; } = default!;
    public decimal TargetValue { get; set; }
    public DateOnly TargetDate { get; set; }
    public decimal MonthlyContribution { get; set; }
    /// <summary>Annualised expected return as a decimal (0.07 = 7%/yr).</summary>
    public decimal ExpectedReturn { get; set; } = 0.07m;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
