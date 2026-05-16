namespace Stocky.Api.Domain;

// M14 #115 — per-position journal entries / notes.
public class PositionNote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = string.Empty;
    public Guid? PortfolioId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

// M14 #112 — append-only audit trail of user-initiated mutations.
public class AuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string Action { get; set; } = string.Empty;       // e.g. "create", "update", "delete"
    public string Resource { get; set; } = string.Empty;     // e.g. "Portfolio", "Transaction"
    public string? ResourceId { get; set; }
    public string? Method { get; set; }                      // HTTP verb
    public string? Path { get; set; }                        // request path
    public int? StatusCode { get; set; }
    public string? ClientIp { get; set; }
    public string? UserAgent { get; set; }
    public string? Details { get; set; }                     // optional JSON blob
}

// M14 #116 — curated allocation templates users can apply to seed a portfolio.
public class ModelTemplateAllocation
{
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AssetClass { get; set; } = string.Empty;   // "Equity", "Bond", "Cash"
    public decimal WeightPercent { get; set; }               // 0–100
}

public class ModelPortfolioTemplate
{
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Risk { get; set; } = "Moderate";          // "Conservative" | "Moderate" | "Aggressive"
    public IReadOnlyList<ModelTemplateAllocation> Allocations { get; set; } = Array.Empty<ModelTemplateAllocation>();
}
