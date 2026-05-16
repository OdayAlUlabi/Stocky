using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Stocky.Api.Dtos;

namespace Stocky.Api.Services;

/// <summary>
/// Finnhub-backed market data provider. Falls back to <see cref="StubMarketDataProvider"/>
/// for any symbol the upstream API does not return (or when the network fails),
/// and caches quotes in IMemoryCache for 30 seconds so the QuoteRefresher loop
/// stays well under the free-tier rate limit (60 calls/minute).
/// </summary>
public sealed class FinnhubMarketDataProvider(
    HttpClient http,
    IMemoryCache cache,
    StubMarketDataProvider fallback,
    ILogger<FinnhubMarketDataProvider> log) : IMarketDataProvider
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public async Task<IReadOnlyList<QuoteDto>> GetQuotesAsync(IReadOnlyCollection<string> symbols, CancellationToken ct = default)
    {
        var result = new List<QuoteDto>(symbols.Count);
        var missing = new List<string>();
        foreach (var s in symbols.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (cache.TryGetValue($"quote:{s}", out QuoteDto? cached) && cached is not null)
            {
                result.Add(cached);
            }
            else
            {
                missing.Add(s.ToUpperInvariant());
            }
        }

        foreach (var sym in missing)
        {
            try
            {
                var resp = await http.GetFromJsonAsync<FinnhubQuote>($"quote?symbol={Uri.EscapeDataString(sym)}", ct);
                if (resp is null || resp.CurrentPrice <= 0)
                {
                    // Unknown symbol on Finnhub (e.g. warrants like OPENZ) — fall back.
                    var stub = await fallback.GetQuotesAsync(new[] { sym }, ct);
                    var sq = stub[0];
                    cache.Set($"quote:{sym}", sq, CacheTtl);
                    result.Add(sq);
                    continue;
                }
                var asOf = resp.Timestamp > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(resp.Timestamp)
                    : DateTimeOffset.UtcNow;
                var pct = resp.PercentChange ?? (resp.PreviousClose == 0 ? 0 : (resp.CurrentPrice - resp.PreviousClose) / resp.PreviousClose * 100m);
                var quote = new QuoteDto(sym, resp.CurrentPrice, resp.Change ?? (resp.CurrentPrice - resp.PreviousClose), pct, asOf);
                cache.Set($"quote:{sym}", quote, CacheTtl);
                result.Add(quote);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Finnhub quote fetch failed for {Symbol}; falling back to stub.", sym);
                var stub = await fallback.GetQuotesAsync(new[] { sym }, ct);
                result.Add(stub[0]);
            }
        }
        return result;
    }

    public Task<IReadOnlyList<NewsItemDto>> GetNewsAsync(IReadOnlyCollection<string>? symbols, int limit, CancellationToken ct = default)
        // News and earnings stay on the stub for now to keep the free-tier
        // request budget for quotes.
        => fallback.GetNewsAsync(symbols, limit, ct);

    public Task<IReadOnlyList<EarningsEventDto>> GetEarningsAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
        => fallback.GetEarningsAsync(from, to, ct);

    private sealed record FinnhubQuote(
        [property: JsonPropertyName("c")] decimal CurrentPrice,
        [property: JsonPropertyName("d")] decimal? Change,
        [property: JsonPropertyName("dp")] decimal? PercentChange,
        [property: JsonPropertyName("h")] decimal High,
        [property: JsonPropertyName("l")] decimal Low,
        [property: JsonPropertyName("o")] decimal Open,
        [property: JsonPropertyName("pc")] decimal PreviousClose,
        [property: JsonPropertyName("t")] long Timestamp);
}
