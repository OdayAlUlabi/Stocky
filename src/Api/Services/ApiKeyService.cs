using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;

namespace Stocky.Api.Services;

/// <summary>
/// M14 #91 — Issues and validates user-bearer API keys for the public REST API.
/// Plaintext keys are SHA-256 hashed before storage; the original value is
/// returned to the caller only once at creation.
/// </summary>
public class ApiKeyService(StockyDbContext db)
{
    public record GeneratedKey(ApiKey Record, string Plaintext);

    public async Task<GeneratedKey> GenerateAsync(string ownerId, string name, string scopes = "read", DateTimeOffset? expiresAt = null, CancellationToken ct = default)
    {
        // 24 random bytes ~ 192-bit secret, base64-url-encoded.
        var secret = ToBase64Url(RandomNumberGenerator.GetBytes(24));
        var idPart = ToBase64Url(RandomNumberGenerator.GetBytes(6)); // visible prefix
        var plaintext = $"sk_{idPart}_{secret}";
        var prefix = $"sk_{idPart}";
        var record = new ApiKey
        {
            OwnerId = ownerId,
            Name = string.IsNullOrWhiteSpace(name) ? "Untitled" : name.Trim(),
            Prefix = prefix,
            HashedKey = Hash(plaintext),
            Scopes = string.IsNullOrWhiteSpace(scopes) ? "read" : scopes.Trim().ToLowerInvariant(),
            ExpiresAt = expiresAt
        };
        db.ApiKeys.Add(record);
        await db.SaveChangesAsync(ct);
        return new GeneratedKey(record, plaintext);
    }

    public async Task<ApiKey?> ValidateAsync(string plaintext, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(plaintext) || !plaintext.StartsWith("sk_", StringComparison.Ordinal))
            return null;
        var hash = Hash(plaintext);
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.HashedKey == hash, ct);
        if (key is null) return null;
        if (key.RevokedAt is not null) return null;
        if (key.ExpiresAt is not null && key.ExpiresAt <= DateTimeOffset.UtcNow) return null;
        key.LastUsedAt = DateTimeOffset.UtcNow;
        try { await db.SaveChangesAsync(ct); } catch { /* best effort */ }
        return key;
    }

    public Task<List<ApiKey>> ListAsync(string ownerId, CancellationToken ct = default) =>
        db.ApiKeys.Where(k => k.OwnerId == ownerId).OrderByDescending(k => k.CreatedAt).ToListAsync(ct);

    public async Task<bool> RevokeAsync(Guid id, string ownerId, CancellationToken ct = default)
    {
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id && k.OwnerId == ownerId, ct);
        if (key is null || key.RevokedAt is not null) return false;
        key.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    internal static string Hash(string plaintext)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plaintext));
        return Convert.ToHexString(bytes);
    }

    private static string ToBase64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
