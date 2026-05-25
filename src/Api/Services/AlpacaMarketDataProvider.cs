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
    IHttpClientFactory httpFactory,
    IMemoryCache cache,
    IProviderCache distributedCache,
    StubMarketDataProvider fallback,
    ILogger<AlpacaMarketDataProvider> log) : IMarketDataProvider
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(5);

    // Two HttpClients: data API for snapshots/bars/news, trading API for
    // /v2/assets/{symbol} (reference data). Configured by name in
    // StockyServicesExtensions so credentials and base URLs live in one place.
    private HttpClient http => httpFactory.CreateClient("Alpaca:Data");
    private HttpClient trading => httpFactory.CreateClient("Alpaca:Trading");

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
            if (quote is not null)
            {
                cache.Set($"quote:{sym}", quote, CacheTtl);
                result.Add(quote);
            }
        }

        // Any symbols Alpaca didn't return (or that built a null quote) get
        // resolved against the stub provider. Do these in parallel — each call
        // is independent and `StubMarketDataProvider` is thread-safe.
        var stillMissing = missing
            .Where(s => !cache.TryGetValue($"quote:{s}", out QuoteDto? _))
            .ToList();
        if (stillMissing.Count > 0)
        {
            var stubResults = await Task.WhenAll(stillMissing.Select(async sym =>
            {
                var stub = await fallback.GetQuotesAsync(new[] { sym }, ct);
                return (sym, quote: stub[0]);
            }));
            foreach (var (sym, quote) in stubResults)
            {
                cache.Set($"quote:{sym}", quote, CacheTtl);
                result.Add(quote);
            }
        }
        return result;
    }

    public async Task<IReadOnlyList<NewsItemDto>> GetNewsAsync(IReadOnlyCollection<string>? symbols, int limit, CancellationToken ct = default)
    {
        // Alpaca News API v1beta1: https://data.alpaca.markets/v1beta1/news?symbols=...&limit=...&sort=desc
        var symList = symbols?.Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var cacheKey = $"news:{(symList is null ? "*" : string.Join(',', symList.OrderBy(s => s)))}:{limit}";
        var hit = await distributedCache.GetAsync<List<NewsItemDto>>(cacheKey, ct);
        if (hit is not null) return hit;

        try
        {
            var qs = $"v1beta1/news?limit={Math.Clamp(limit, 1, 50)}&sort=desc&include_content=false";
            if (symList is not null && symList.Count > 0)
            {
                qs += $"&symbols={Uri.EscapeDataString(string.Join(',', symList))}";
            }
            var resp = await http.GetFromJsonAsync<AlpacaNewsResponse>(qs, ct);
            var items = new List<NewsItemDto>();
            if (resp?.News is not null)
            {
                foreach (var n in resp.News)
                {
                    // Emit one row per symbol the article tags so the per-symbol filter
                    // in the News page works without duplicating the article record on the
                    // database side. If the article has no symbols, emit a single un-tagged row.
                    var tagged = (n.Symbols is { Count: > 0 } ? n.Symbols : new List<string> { string.Empty })
                        .Where(s => symList is null || symList.Contains(s, StringComparer.OrdinalIgnoreCase) || s == string.Empty)
                        .DefaultIfEmpty(string.Empty);
                    foreach (var sym in tagged)
                    {
                        items.Add(new NewsItemDto(
                            Id: n.Id,
                            Headline: n.Headline ?? "(no headline)",
                            Summary: n.Summary,
                            Source: string.IsNullOrWhiteSpace(n.Source) ? "alpaca" : n.Source!,
                            Url: n.Url,
                            Symbol: string.IsNullOrWhiteSpace(sym) ? null : sym,
                            PublishedAt: n.CreatedAt ?? DateTimeOffset.UtcNow,
                            Category: "market"));
                    }
                }
            }
            // Dedup (Id, Symbol) and cap at limit.
            var result = items
                .GroupBy(i => (i.Id, i.Symbol))
                .Select(g => g.First())
                .OrderByDescending(i => i.PublishedAt)
                .Take(limit)
                .ToList();
            await distributedCache.SetAsync(cacheKey, result, TimeSpan.FromMinutes(5), ct);
            return result;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Alpaca news fetch failed; falling back to stub.");
            return await fallback.GetNewsAsync(symbols, limit, ct);
        }
    }

    public Task<IReadOnlyList<EarningsEventDto>> GetEarningsAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
        => fallback.GetEarningsAsync(from, to, ct);

    public async Task<IReadOnlyList<AssetProfileDto>> GetAssetProfilesAsync(
        IReadOnlyCollection<string> symbols, CancellationToken ct = default)
    {
        if (symbols.Count == 0) return Array.Empty<AssetProfileDto>();

        var symList = symbols
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Alpaca's /v2/assets/{symbol} is single-symbol; fan out with bounded
        // parallelism (~8 in flight) so 100 symbols cost ~13 round-trips of
        // wall time instead of 100. Per-symbol responses are cached for 24h
        // in memory since asset reference data rarely changes.
        var results = new System.Collections.Concurrent.ConcurrentBag<AssetProfileDto>();
        using var gate = new SemaphoreSlim(8);
        var tasks = symList.Select(async sym =>
        {
            await gate.WaitAsync(ct);
            try
            {
                if (cache.TryGetValue($"asset:{sym}", out AssetProfileDto? cached) && cached is not null)
                {
                    results.Add(cached);
                    return;
                }
                AlpacaAsset? asset = null;
                try
                {
                    asset = await trading.GetFromJsonAsync<AlpacaAsset>(
                        $"v2/assets/{Uri.EscapeDataString(sym)}", ct);
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Alpaca doesn't know this symbol (e.g. private tickers, OTC
                    // spin-off warrants). Cache a null marker so we don't hammer
                    // the API on every refresh.
                    cache.Set($"asset:{sym}", (AssetProfileDto?)null, TimeSpan.FromHours(24));
                    return;
                }
                catch (Exception ex)
                {
                    log.LogDebug(ex, "Alpaca /v2/assets/{Symbol} failed", sym);
                    return;
                }
                if (asset is null) return;

                var profile = new AssetProfileDto(
                    Symbol: sym,
                    Name: asset.Name,
                    Exchange: asset.Exchange,
                    AssetClass: MapAssetClass(asset.Class),
                    Status: asset.Status,
                    IsTradable: asset.Tradable,
                    IsFractionable: asset.Fractionable,
                    IsShortable: asset.Shortable,
                    IsMarginable: asset.Marginable,
                    IsEasyToBorrow: asset.EasyToBorrow,
                    MaintenanceMarginRequirement: asset.MaintenanceMarginRequirement);
                cache.Set($"asset:{sym}", profile, TimeSpan.FromHours(24));
                results.Add(profile);
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(tasks);
        return results.ToList();
    }

    private static string? MapAssetClass(string? alpacaClass) => alpacaClass?.ToLowerInvariant() switch
    {
        "us_equity" => "Equity",
        "crypto" => "Crypto",
        "us_option" => "Option",
        null or "" => null,
        _ => alpacaClass
    };

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<DailyBarDto>>> GetDailyBarsAsync(
        IReadOnlyCollection<string> symbols, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (symbols.Count == 0 || from > to)
        {
            return new Dictionary<string, IReadOnlyList<DailyBarDto>>(StringComparer.OrdinalIgnoreCase);
        }

        var symList = symbols.Select(s => s.ToUpperInvariant()).Distinct().ToList();
        var cacheKey = $"bars:{from:yyyyMMdd}:{to:yyyyMMdd}:{string.Join(',', symList.OrderBy(s => s))}";
        var hit = await distributedCache.GetAsync<Dictionary<string, List<DailyBarDto>>>(cacheKey, ct);
        if (hit is not null)
        {
            return hit.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<DailyBarDto>)kv.Value, StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, List<DailyBarDto>>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in symList) result[s] = new List<DailyBarDto>();

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
                        if (!result.TryGetValue(sym, out var existing))
                        {
                            existing = new List<DailyBarDto>();
                            result[sym] = existing;
                        }
                        foreach (var b in bars)
                            existing.Add(new DailyBarDto(
                                DateOnly.FromDateTime(b.Timestamp.UtcDateTime),
                                b.Close,
                                b.Open,
                                b.High,
                                b.Low,
                                b.Volume));
                    }
                }
                pageToken = resp?.NextPageToken;
            } while (!string.IsNullOrEmpty(pageToken));
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Alpaca bars fetch failed for {Count} symbols {From}..{To}", symList.Count, from, to);
        }

        await distributedCache.SetAsync(cacheKey, result, TimeSpan.FromHours(6), ct);
        return result.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<DailyBarDto>)kv.Value, StringComparer.OrdinalIgnoreCase);
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
        [JsonPropertyName("o")] public decimal? Open { get; set; }
        [JsonPropertyName("h")] public decimal? High { get; set; }
        [JsonPropertyName("l")] public decimal? Low { get; set; }
        [JsonPropertyName("c")] public decimal Close { get; set; }
        [JsonPropertyName("v")] public long? Volume { get; set; }
        [JsonPropertyName("t")] public DateTimeOffset Timestamp { get; set; }
    }

    private sealed class AlpacaBarsResponse
    {
        [JsonPropertyName("bars")] public Dictionary<string, List<AlpacaBar>>? Bars { get; set; }
        [JsonPropertyName("next_page_token")] public string? NextPageToken { get; set; }
    }

    private sealed class AlpacaNewsItem
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("headline")] public string? Headline { get; set; }
        [JsonPropertyName("summary")] public string? Summary { get; set; }
        [JsonPropertyName("source")] public string? Source { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("symbols")] public List<string>? Symbols { get; set; }
        [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    }

    private sealed class AlpacaNewsResponse
    {
        [JsonPropertyName("news")] public List<AlpacaNewsItem>? News { get; set; }
        [JsonPropertyName("next_page_token")] public string? NextPageToken { get; set; }
    }

    private sealed class AlpacaAsset
    {
        [JsonPropertyName("symbol")] public string? Symbol { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("exchange")] public string? Exchange { get; set; }
        [JsonPropertyName("class")] public string? Class { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("tradable")] public bool? Tradable { get; set; }
        [JsonPropertyName("marginable")] public bool? Marginable { get; set; }
        [JsonPropertyName("shortable")] public bool? Shortable { get; set; }
        [JsonPropertyName("easy_to_borrow")] public bool? EasyToBorrow { get; set; }
        [JsonPropertyName("fractionable")] public bool? Fractionable { get; set; }
        [JsonPropertyName("maintenance_margin_requirement")] public decimal? MaintenanceMarginRequirement { get; set; }
    }
}
