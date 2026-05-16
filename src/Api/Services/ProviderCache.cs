using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace Stocky.Api.Services;

/// <summary>
/// Thin JSON wrapper over <see cref="IDistributedCache"/> used by market data
/// providers so multiple App Service instances share the same cached payload
/// (Alpaca rate limits and bandwidth costs apply per-process otherwise).
/// When no Redis connection is configured the host registers the in-memory
/// distributed cache, so this works seamlessly in dev.
/// </summary>
public interface IProviderCache
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default) where T : class;
}

public sealed class ProviderCache(IDistributedCache cache, ILogger<ProviderCache> log) : IProviderCache
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
    {
        try
        {
            var bytes = await cache.GetAsync(key, ct);
            if (bytes is null || bytes.Length == 0) return null;
            return JsonSerializer.Deserialize<T>(bytes, Json);
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "Distributed cache GET failed for {Key}; treating as miss.", key);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default) where T : class
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
            await cache.SetAsync(key, bytes, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, ct);
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "Distributed cache SET failed for {Key}; ignored.", key);
        }
    }
}
