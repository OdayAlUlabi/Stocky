namespace Stocky.Api.Domain;

/// <summary>
/// M11 #54 — revocable read-only sharing token for a portfolio. The Token
/// column is a URL-safe random string; rows can be revoked (soft-disable)
/// or expire automatically via <see cref="ExpiresAt"/>.
/// </summary>
public class ShareToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    // M11 #54 hardening — plaintext token is shown to the caller exactly once at
    // creation time. Persisted state is the SHA-256 hash (hex). TokenPrefix is
    // a short non-secret display id so users can identify the link in lists.
    public string TokenHash { get; set; } = default!;
    public string TokenPrefix { get; set; } = default!;
    public Guid PortfolioId { get; set; }
    public Portfolio? Portfolio { get; set; }
    public string OwnerId { get; set; } = default!;
    public string? Label { get; set; }                      // optional human name (e.g., "Advisor — Jane")
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public int ViewCount { get; set; }
    public DateTimeOffset? LastViewedAt { get; set; }
    public bool IncludeTransactions { get; set; } = false;  // optional widening
    public bool IncludeCostBasis { get; set; } = false;     // hide P&L by default for advisors

    public bool IsActive(DateTimeOffset now)
        => RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);
}

public enum ReportType
{
    CapitalGains = 0,
    WashSales = 1,
    Dividends = 2
}

public enum ReportFormat
{
    Csv = 0,
    Pdf = 1
}

public enum ReportCadence
{
    OnDemand = 0,
    Weekly = 1,
    Monthly = 2,
    Quarterly = 3
}

/// <summary>
/// M11 #55 — user-defined recurring export of a tax/dividend report. The
/// background sweep runs whenever <c>NextRunUtc &lt;= now</c>, generates the
/// requested artifact, persists a <see cref="ReportDelivery"/> row, and bumps
/// <c>NextRunUtc</c> according to <c>Cadence</c>.
/// </summary>
public class ReportSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!;
    public Guid PortfolioId { get; set; }
    public Portfolio? Portfolio { get; set; }
    public ReportType Type { get; set; }
    public ReportFormat Format { get; set; } = ReportFormat.Csv;
    public ReportCadence Cadence { get; set; } = ReportCadence.Monthly;
    public string? Email { get; set; }                      // optional recipient stub
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset NextRunUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastRunUtc { get; set; }
}

/// <summary>History of generated artifacts (#55). The Body column holds the
/// rendered CSV or text-PDF inline so the UI can re-download without
/// re-running the report.</summary>
public class ReportDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ScheduleId { get; set; }                   // null for on-demand exports
    public ReportSchedule? Schedule { get; set; }
    public string OwnerId { get; set; } = default!;
    public Guid PortfolioId { get; set; }
    public ReportType Type { get; set; }
    public ReportFormat Format { get; set; }
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
    public string FileName { get; set; } = default!;
    public string ContentType { get; set; } = default!;
    public int SizeBytes { get; set; }
    public string Body { get; set; } = default!;            // rendered artifact (csv text / pdf text)
    public string? Trigger { get; set; }                    // "schedule" | "ondemand"
    public string? Channel { get; set; }                    // "inbox" | "email"
}
