using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Stocky.Api.Dtos;

namespace Stocky.Api.Services;

/// <summary>
/// Alpaca Market Data v2 provider. Uses the multi-symbol /v2/stocks/snapshots
/// endpoint so the QuoteRefresher loop costs one HTTP call regardless of how
/// many symbols are tracked. Falls back to <see cref="StubMarketDataProvider"/>
/// for symbols Alpaca does not return (e.g. brand-new spin-off warrants like
/// OPENZ/W/L) or whenever the network fails, and caches each quote in
/// IMemoryCache for 30 seconds.
/// </summary>
public sealed class AlpacaMarketDataProvider(
    HttpClient http,
    IMemoryCache cache,
    StubMarketDataProvider fallback,
    ILogger<AlpacaMarketDataProvider> log) : IMarketDataProvider
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(5);

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

        if (missing.Count == 0) return result;

        Dictionary<string, AlpacaSnapshot>? snapshots = null;
        try
        {
            var url = $"v2/stocks/snapshots?symbols={Uri.EscapeDataString(string.Join(',', missing))}";
            snapshots = await http.GetFromJsonAsync<Dictionary<string, AlpacaSnapshot>>(url, ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Alpaca snapshot fetch failed for {Count} symbols; falling back to stub.", missing.Count);
        }

        foreach (var sym in missing)
        {
            QuoteDto? quote = null;
            if (snapshots is not null && snapshots.TryGetValue(sym, out var snap))
            {
                quote = TryBuildQuote(sym, snap);
            }
            if (quote is null)
            {
                var stub = await fallback.GetQuotesAsync(new[] { sym }, ct);
                quote = stub[0];
            }
            cache.Set($"quote:{sym}", quote, CacheTtl);
            result.Add(quote);
        }
        return result;
    }

    public Task<IReadOnlyList<NewsItemDto>> GetNewsAsync(IReadOnlyCollection<string>? symbols, int limit, CancellationToken ct = default)
        // News and earnings stay on the stub for now; Alpaca news is a separate
        // endpoint and earnings are not in the market data product.
        => fallback.GetNewsAsync(symbols, limit, ct);

    public Task<IReadOnlyList<EarningsEventDto>> GetEarningsAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
        => fallback.GetEarningsAsync(from, to, ct);

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<DailyBarDto>>> GetDailyBarsAsync(
        IReadOnlyCollection<string> symbols, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var result = new Dictionary<string, IReadOnlyList<DailyBarDto>>(StringComparer.OrdinalIgnoreCase);
        if (symbols.Count == 0 || from > to) return result;

        var symList = symbols.Select(s => s.ToUpperInvariant()).Distinct().ToList();
        var cacheKey = $"bars:{from:yyyyMMdd}:{to:yyyyMMdd}:{string.Join(',', symList.OrderBy(s => s))}";
        if (cache.TryGetValue(cacheKey, out IReadOnlyDictionary<string, IReadOnlyList<DailyBarDto>>? cached) && cached is not null)
        {
            return cached;
        }

        foreach (var s in symList) result[s] = Array.Empty<DailyBarDto>();

        try
        {
            string? pageToken = null;
            do
            {
                var url = $"v2/stocks/bars?symbols={Uri.EscapeDataString(string.Join(',', symList))}"
                          + $"&timeframe=1Day&start={from:yyyy-MM-dd}&end={to:yyyy-MM-dd}"
                          + $"&adjustment=split&feed=iex&limit=10000"
                          + (pageToken is null ? string.Empty : $"&page_token={Uri.EscapeDataString(pageToken)}");
                var resp = await http.GetFromJsonAsync<AlpacaBarsResponse>(url, ct);
                if (resp?.Bars is { Count: > 0 })
                {
                    foreach (var (sym, bars) in resp.Bars)
                    {
                        var existing = result.TryGetValue(sym, out var prev) ? prev.ToList() : new List<DailyBarDto>();
                        foreach (var b in bars)
                            existing.Add(new DailyBarDto(DateOnly.FromDateTime(b.Timestamp.UtcDateTime), b.Close));
                        result[sym] = existing;
                    }
                }
                pageToken = resp?.NextPageToken;
            } while (!string.IsNullOrEmpty(pageToken));
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Alpaca bars fetch failed for {Count} symbols {From}..{To}", symList.Count, from, to);
        }

        IReadOnlyDictionary<string, IReadOnlyList<DailyBarDto>> readonlyResult = result;
        cache.Set(cacheKey, readonlyResult, TimeSpan.FromHours(6));
        return readonlyResult;
    }

    private static QuoteDto? TryBuildQuote(string symbol, AlpacaSnapshot snap)
    {
        // Prefer the latest trade price; fall back to the daily bar close.
        var price = snap.LatestTrade?.Price ?? snap.MinuteBar?.Close ?? snap.DailyBar?.Close;
        if (price is null or <= 0) return null;
        var prevClose = snap.PrevDailyBar?.Close ?? snap.DailyBar?.Close ?? price.Value;
        var change = price.Value - prevClose;
        var pct = prevClose == 0 ? 0 : Math.Round(change / prevClose * 100m, 2);
        var asOf = snap.LatestTrade?.Timestamp ?? snap.MinuteBar?.Timestamp ?? snap.DailyBar?.Timestamp ?? DateTimeOffset.UtcNow;
        return new QuoteDto(symbol, Math.Round(price.Value, 4), Math.Round(change, 4), pct, asOf);
    }

    private sealed class AlpacaSnapshot
    {
        [JsonPropertyName("latestTrade")] public AlpacaTrade? LatestTrade { get; set; }
        [JsonPropertyName("minuteBar")] public AlpacaBar? MinuteBar { get; set; }
        [JsonPropertyName("dailyBar")] public AlpacaBar? DailyBar { get; set; }
        [JsonPropertyName("prevDailyBar")] public AlpacaBar? PrevDailyBar { get; set; }
    }

    private sealed class AlpacaTrade
    {
        [JsonPropertyName("p")] public decimal Price { get; set; }
        [JsonPropertyName("t")] public DateTimeOffset Timestamp { get; set; }
    }

    private sealed class AlpacaBar
    {
        [JsonPropertyName("c")] public decimal Close { get; set; }
        [JsonPropertyName("t")] public DateTimeOffset Timestamp { get; set; }
    }

    private sealed class AlpacaBarsResponse
    {
        [JsonPropertyName("bars")] public Dictionary<string, List<AlpacaBar>>? Bars { get; set; }
        [JsonPropertyName("next_page_token")] public string? NextPageToken { get; set; }
    }
}
