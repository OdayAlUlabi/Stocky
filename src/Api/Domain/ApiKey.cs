using System.ComponentModel.DataAnnotations.Schema;

namespace Stocky.Api.Domain;

/// <summary>
/// M14 #91 — User-issued bearer key used for the public REST API.
/// Plaintext value (sk_prefix_secret) is shown to the user once at creation;
/// we persist only the SHA-256 hash plus a short prefix for display.
/// </summary>
public class ApiKey
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!;
    public string Name { get; set; } = "";
    public string Prefix { get; set; } = default!;   // e.g. "sk_abc123" — first 12 chars, for display only.
    public string HashedKey { get; set; } = default!; // SHA-256 hex of the full plaintext.
    public string Scopes { get; set; } = "read";    // CSV: "read", "write" (future). Currently read only.
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }

    [NotMapped]
    public bool IsActive => RevokedAt is null && (ExpiresAt is null || ExpiresAt > DateTimeOffset.UtcNow);
}
