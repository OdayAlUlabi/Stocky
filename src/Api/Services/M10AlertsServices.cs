using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;

namespace Stocky.Api.Services;

// M10 — Advanced Alerts services
// -----------------------------------------------------------------------------
// Layout:
//   - TechnicalIndicatorService : SMA + RSI from daily bars
//   - IInsiderTradeProvider     : abstraction + deterministic stub
//   - IAlertChannel             : Inbox / Email / Push / Webhook delivery
//   - AlertDispatcher           : fan out a trip event to channels + write history
//   - EarningsAlertEvaluator    : days-before-earnings sweeper (#48)
//   - NewsAlertEvaluator        : keyword + sentiment sweeper (#49)
//   - DriftAlertEvaluator       : drift vs rebalance target sweeper (#50)
//   - InsiderAlertEvaluator     : cluster buy/sell sweeper (#51)
//   - AlertSweepJob             : hosted service that wakes the four sweepers

/// <summary>M10 #47 — pure-function indicators over a chronological bar series.</summary>
public sealed class TechnicalIndicatorService
{
    public IReadOnlyList<decimal?> Ema(IReadOnlyList<decimal> closes, int period)
    {
        if (period <= 0) throw new ArgumentOutOfRangeException(nameof(period));
        var result = new decimal?[closes.Count];
        if (closes.Count < period) return result;

        decimal sma = 0m;
        for (var i = 0; i < period; i++)
            sma += closes[i];
        var ema = sma / period;
        result[period - 1] = Math.Round(ema, 6);

        var multiplier = 2m / (period + 1m);
        for (var i = period; i < closes.Count; i++)
        {
            ema = ((closes[i] - ema) * multiplier) + ema;
            result[i] = Math.Round(ema, 6);
        }
        return result;
    }

    public IReadOnlyList<decimal?> Sma(IReadOnlyList<decimal> closes, int period)
    {
        if (period <= 0) throw new ArgumentOutOfRangeException(nameof(period));
        var result = new decimal?[closes.Count];
        if (closes.Count < period) return result;
        decimal sum = 0m;
        for (var i = 0; i < closes.Count; i++)
        {
            sum += closes[i];
            if (i >= period) sum -= closes[i - period];
            if (i >= period - 1) result[i] = Math.Round(sum / period, 6);
        }
        return result;
    }

    public (IReadOnlyList<decimal?> Line, IReadOnlyList<decimal?> Signal, IReadOnlyList<decimal?> Histogram) Macd(
        IReadOnlyList<decimal> closes,
        int fastPeriod = 12,
        int slowPeriod = 26,
        int signalPeriod = 9)
    {
        var fast = Ema(closes, fastPeriod);
        var slow = Ema(closes, slowPeriod);
        var line = new decimal?[closes.Count];
        var signal = new decimal?[closes.Count];
        var hist = new decimal?[closes.Count];

        var lineValues = new List<decimal>();
        var lineIndices = new List<int>();
        for (var i = 0; i < closes.Count; i++)
        {
            if (fast[i] is not { } f || slow[i] is not { } s) continue;
            var value = Math.Round(f - s, 6);
            line[i] = value;
            lineValues.Add(value);
            lineIndices.Add(i);
        }

        var signalValues = Ema(lineValues, signalPeriod);
        for (var i = 0; i < lineValues.Count; i++)
        {
            var idx = lineIndices[i];
            if (signalValues[i] is not { } sig) continue;
            signal[idx] = sig;
            hist[idx] = Math.Round(line[idx]!.Value - sig, 6);
        }

        return (line, signal, hist);
    }

    public IReadOnlyList<decimal?> Atr(IReadOnlyList<decimal> highs, IReadOnlyList<decimal> lows, IReadOnlyList<decimal> closes, int period = 14)
    {
        if (period <= 0) throw new ArgumentOutOfRangeException(nameof(period));
        if (highs.Count != lows.Count || highs.Count != closes.Count)
            throw new ArgumentException("Input series must have the same length.");

        var result = new decimal?[closes.Count];
        if (closes.Count <= period) return result;

        var trueRanges = new decimal[closes.Count];
        trueRanges[0] = highs[0] - lows[0];
        for (var i = 1; i < closes.Count; i++)
        {
            var highLow = highs[i] - lows[i];
            var highClose = Math.Abs(highs[i] - closes[i - 1]);
            var lowClose = Math.Abs(lows[i] - closes[i - 1]);
            trueRanges[i] = Math.Max(highLow, Math.Max(highClose, lowClose));
        }

        decimal sum = 0m;
        for (var i = 0; i < period; i++)
            sum += trueRanges[i];
        result[period - 1] = Math.Round(sum / period, 6);

        for (var i = period; i < closes.Count; i++)
        {
            var prev = result[i - 1]!.Value;
            result[i] = Math.Round(((prev * (period - 1)) + trueRanges[i]) / period, 6);
        }
        return result;
    }

    public (IReadOnlyList<decimal?> Upper, IReadOnlyList<decimal?> Middle, IReadOnlyList<decimal?> Lower) BollingerBands(
        IReadOnlyList<decimal> closes,
        int period = 20,
        decimal stdDevMultiplier = 2m)
    {
        if (period <= 1) throw new ArgumentOutOfRangeException(nameof(period));
        var middle = Sma(closes, period);
        var upper = new decimal?[closes.Count];
        var lower = new decimal?[closes.Count];
        for (var i = period - 1; i < closes.Count; i++)
        {
            var start = i - period + 1;
            var slice = closes.Skip(start).Take(period).Select(v => (double)v).ToArray();
            var mean = slice.Average();
            var variance = slice.Sum(v => (v - mean) * (v - mean)) / slice.Length;
            var stdev = Math.Sqrt(variance);
            upper[i] = Math.Round((decimal)(mean + stdev * (double)stdDevMultiplier), 6);
            lower[i] = Math.Round((decimal)(mean - stdev * (double)stdDevMultiplier), 6);
        }
        return (upper, middle, lower);
    }

    /// <summary>Wilder-smoothed RSI (period typically 14).</summary>
    public IReadOnlyList<decimal?> Rsi(IReadOnlyList<decimal> closes, int period = 14)
    {
        if (period <= 1) throw new ArgumentOutOfRangeException(nameof(period));
        var result = new decimal?[closes.Count];
        if (closes.Count <= period) return result;

        decimal gainSum = 0m, lossSum = 0m;
        for (var i = 1; i <= period; i++)
        {
            var diff = closes[i] - closes[i - 1];
            if (diff >= 0) gainSum += diff; else lossSum -= diff;
        }
        var avgGain = gainSum / period;
        var avgLoss = lossSum / period;
        result[period] = ComputeRsi(avgGain, avgLoss);

        for (var i = period + 1; i < closes.Count; i++)
        {
            var diff = closes[i] - closes[i - 1];
            var gain = diff > 0 ? diff : 0m;
            var loss = diff < 0 ? -diff : 0m;
            avgGain = (avgGain * (period - 1) + gain) / period;
            avgLoss = (avgLoss * (period - 1) + loss) / period;
            result[i] = ComputeRsi(avgGain, avgLoss);
        }
        return result;
    }

    private static decimal ComputeRsi(decimal avgGain, decimal avgLoss)
    {
        if (avgLoss == 0m) return 100m;
        var rs = avgGain / avgLoss;
        return Math.Round(100m - (100m / (1m + rs)), 4);
    }

    /// <summary>True if the latest bar is the bar where <c>closes</c> crossed above the SMA.</summary>
    public bool CrossedAboveSma(IReadOnlyList<decimal> closes, int period)
    {
        if (closes.Count < period + 1) return false;
        var sma = Sma(closes, period);
        var sLast = sma[^1]; var sPrev = sma[^2];
        if (sLast is null || sPrev is null) return false;
        return closes[^2] <= sPrev && closes[^1] > sLast;
    }

    public bool CrossedBelowSma(IReadOnlyList<decimal> closes, int period)
    {
        if (closes.Count < period + 1) return false;
        var sma = Sma(closes, period);
        var sLast = sma[^1]; var sPrev = sma[^2];
        if (sLast is null || sPrev is null) return false;
        return closes[^2] >= sPrev && closes[^1] < sLast;
    }

    /// <summary>
    /// Volume-Weighted Average Price (VWAP) — cumulative price×volume / cumulative volume.
    /// Used for mean reversion strategies to identify overbought/oversold conditions.
    /// </summary>
    public IReadOnlyList<decimal?> Vwap(IReadOnlyList<decimal> closes, IReadOnlyList<long> volumes)
    {
        if (closes.Count != volumes.Count)
            throw new ArgumentException("Closes and volumes must have the same length.");
        if (closes.Count == 0) return Array.Empty<decimal?>();

        var result = new decimal?[closes.Count];
        decimal cumulativePriceVolume = 0m;
        decimal cumulativeVolume = 0m;

        for (var i = 0; i < closes.Count; i++)
        {
            var volume = (decimal)volumes[i];
            cumulativePriceVolume += closes[i] * volume;
            cumulativeVolume += volume;

            if (cumulativeVolume > 0m)
                result[i] = Math.Round(cumulativePriceVolume / cumulativeVolume, 6);
        }

        return result;
    }

    /// <summary>
    /// Calculate the percentage deviation of current price from VWAP.
    /// Positive = price above VWAP (potential overbought), Negative = below VWAP (potential oversold).
    /// </summary>
    public decimal? VwapDeviation(decimal currentPrice, decimal vwap)
    {
        if (vwap <= 0m) return null;
        return Math.Round(((currentPrice - vwap) / vwap) * 100m, 4);
    }
}

// ---------------------------------------------------------------------------
// Insider provider (stub seeds deterministic rows on first call)

public interface IInsiderTradeProvider
{
    Task<IReadOnlyList<InsiderTrade>> GetRecentTradesAsync(string symbol, int days, CancellationToken ct = default);
}

public sealed class StubInsiderTradeProvider : IInsiderTradeProvider
{
    private readonly StockyDbContext _db;
    public StubInsiderTradeProvider(StockyDbContext db) { _db = db; }

    public async Task<IReadOnlyList<InsiderTrade>> GetRecentTradesAsync(string symbol, int days, CancellationToken ct = default)
    {
        symbol = (symbol ?? string.Empty).ToUpperInvariant();
        var since = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, days));
        var existing = await _db.InsiderTrades
            .Where(t => t.Symbol == symbol && t.FiledAt >= since)
            .OrderByDescending(t => t.FiledAt)
            .ToListAsync(ct);
        if (existing.Count > 0) return existing;

        // Seed a deterministic cluster so the UI + alerts have data to fire on.
        var rng = new Random(Hash(symbol));
        var seeded = new List<InsiderTrade>();
        var anchor = DateTimeOffset.UtcNow.AddDays(-rng.Next(1, Math.Min(10, days)));
        var clusterBuy = rng.NextDouble() > 0.5;
        var count = rng.Next(3, 7);
        for (var i = 0; i < count; i++)
        {
            seeded.Add(new InsiderTrade
            {
                Symbol = symbol,
                InsiderName = $"Insider {rng.Next(1, 99):D2}",
                Relation = i % 2 == 0 ? "Officer" : "Director",
                TransactionType = clusterBuy ? "Buy" : "Sell",
                Shares = Math.Round((decimal)(rng.NextDouble() * 10_000 + 500), 0),
                Price = Math.Round((decimal)(rng.NextDouble() * 200 + 20), 2),
                FiledAt = anchor.AddDays(-i)
            });
        }
        _db.InsiderTrades.AddRange(seeded);
        await _db.SaveChangesAsync(ct);
        return seeded;
    }

    private static int Hash(string s)
    {
        var h = 0;
        foreach (var c in s) h = unchecked(h * 31 + c);
        return Math.Abs(h);
    }
}

// ---------------------------------------------------------------------------
// Channels (#52 multi-channel delivery)

public interface IAlertChannel
{
    string Name { get; }
    Task DeliverAsync(Alert alert, string message, CancellationToken ct);
}

public sealed class InboxChannel : IAlertChannel
{
    public string Name => "Inbox";
    // The dispatcher writes the AlertEvent row regardless, so this is a no-op.
    public Task DeliverAsync(Alert alert, string message, CancellationToken ct) => Task.CompletedTask;
}

public sealed class EmailChannel(ILogger<EmailChannel> logger) : IAlertChannel
{
    public string Name => "Email";
    public Task DeliverAsync(Alert alert, string message, CancellationToken ct)
    {
        // Real impl would call SendGrid / ACS. Stubbed to a structured log so
        // smoke tests can verify the dispatcher fanned out without secrets.
        logger.LogInformation("ALERT.EMAIL {Owner} {Symbol} {Message}", alert.OwnerId, alert.Symbol, message);
        return Task.CompletedTask;
    }
}

public sealed class PushChannel(ILogger<PushChannel> logger) : IAlertChannel
{
    public string Name => "Push";
    public Task DeliverAsync(Alert alert, string message, CancellationToken ct)
    {
        logger.LogInformation("ALERT.PUSH {Owner} {Symbol} {Message}", alert.OwnerId, alert.Symbol, message);
        return Task.CompletedTask;
    }
}

public sealed class WebhookChannel(IHttpClientFactory httpFactory, ILogger<WebhookChannel> logger) : IAlertChannel
{
    public string Name => "Webhook";

    /// <summary>
    /// SSRF guard: only allow https URLs to publicly-routable hosts. Rejects
    /// loopback / link-local / private (RFC1918) / CGNAT / IPv6 ULA targets
    /// and any hostname that resolves to one. Returns the validated Uri or null.
    /// </summary>
    internal static async Task<Uri?> ValidateWebhookUrlAsync(string raw, CancellationToken ct)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps) return null;
        if (uri.IsDefaultPort is false && uri.Port != 443) return null;

        System.Net.IPAddress[] addrs;
        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        {
            addrs = new[] { System.Net.IPAddress.Parse(uri.Host) };
        }
        else
        {
            try { addrs = await System.Net.Dns.GetHostAddressesAsync(uri.Host, ct); }
            catch { return null; }
        }
        if (addrs.Length == 0) return null;
        foreach (var ip in addrs) if (!IsPubliclyRoutable(ip)) return null;
        return uri;
    }

    private static bool IsPubliclyRoutable(System.Net.IPAddress ip)
    {
        if (System.Net.IPAddress.IsLoopback(ip)) return false;
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            // 0.0.0.0/8, 10/8, 100.64/10 (CGNAT), 127/8, 169.254/16, 172.16/12, 192.168/16, 224/4 multicast, 240/4 reserved
            if (b[0] == 0 || b[0] == 10 || b[0] == 127) return false;
            if (b[0] == 100 && (b[1] & 0xC0) == 64) return false;
            if (b[0] == 169 && b[1] == 254) return false;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return false;
            if (b[0] == 192 && b[1] == 168) return false;
            if (b[0] >= 224) return false;
            return true;
        }
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return false;
            if (ip.Equals(System.Net.IPAddress.IPv6Loopback)) return false;
            var b = ip.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return false;            // fc00::/7 unique-local
            if (b[0] == 0 && b[1] == 0)                          // ::ffff:0:0/96 mapped IPv4
            {
                var allZero = true;
                for (var i = 2; i < 10; i++) if (b[i] != 0) { allZero = false; break; }
                if (allZero && b[10] == 0xFF && b[11] == 0xFF)
                {
                    var mapped = new System.Net.IPAddress(new[] { b[12], b[13], b[14], b[15] });
                    return IsPubliclyRoutable(mapped);
                }
            }
            return true;
        }
        return false;
    }

    public async Task DeliverAsync(Alert alert, string message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(alert.WebhookUrl)) return;
        var target = await ValidateWebhookUrlAsync(alert.WebhookUrl, ct);
        if (target is null)
        {
            logger.LogWarning("Webhook URL rejected by SSRF guard for alert {Id}", alert.Id);
            return;
        }
        try
        {
            var client = httpFactory.CreateClient("stocky-webhook");
            var payload = new
            {
                alertId = alert.Id,
                symbol = alert.Symbol,
                type = alert.Type.ToString(),
                condition = alert.Condition.ToString(),
                threshold = alert.Threshold,
                triggeredAt = DateTimeOffset.UtcNow,
                message
            };
            using var resp = await client.PostAsJsonAsync(target, payload, ct);
            if (!resp.IsSuccessStatusCode)
                logger.LogWarning("Webhook {Url} returned {Status} for alert {Id}", target, (int)resp.StatusCode, alert.Id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Webhook delivery failed for alert {Id}", alert.Id);
        }
    }
}

/// <summary>
/// Fan-out alert dispatcher (#52). Marks the alert Triggered, writes an
/// AlertEvent row (#53), and invokes every channel the alert opted into.
/// </summary>
public sealed class AlertDispatcher(
    StockyDbContext db,
    IEnumerable<IAlertChannel> channels,
    ILogger<AlertDispatcher> logger)
{
    private readonly Dictionary<string, IAlertChannel> _channels =
        channels.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

    public async Task TripAsync(Alert alert, decimal? value, string message, string? context, CancellationToken ct)
    {
        // Skip if snoozed.
        if (alert.SnoozedUntil is { } until && until > DateTimeOffset.UtcNow) return;

        alert.Status = AlertStatus.Triggered;
        alert.TriggeredAt = DateTimeOffset.UtcNow;
        alert.TriggeredValue = value;

        var requested = (alert.Channels ?? "Inbox")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var delivered = new List<string>();
        foreach (var name in requested)
        {
            if (_channels.TryGetValue(name, out var ch))
            {
                try
                {
                    await ch.DeliverAsync(alert, message, ct);
                    delivered.Add(ch.Name);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Channel {Name} delivery failed for {AlertId}", ch.Name, alert.Id);
                }
            }
        }
        if (delivered.Count == 0) delivered.Add("Inbox");

        db.AlertEvents.Add(new AlertEvent
        {
            AlertId = alert.Id,
            OwnerId = alert.OwnerId,
            Symbol = alert.Symbol,
            Type = alert.Type,
            Condition = alert.Condition,
            TriggeredValue = value,
            Message = message,
            Channels = string.Join(",", delivered),
            Context = context
        });
        await db.SaveChangesAsync(ct);
    }
}

// ---------------------------------------------------------------------------
// Evaluators

/// <summary>
/// M10 #47 — extends the price evaluator by reading recent bars for any
/// Technical alerts and checking SMA cross / RSI thresholds against the
/// most recent close.
/// </summary>
public sealed class TechnicalAlertEvaluator(
    StockyDbContext db,
    IAdvancedMarketDataProvider provider,
    TechnicalIndicatorService indicators,
    AlertDispatcher dispatcher,
    ILogger<TechnicalAlertEvaluator> logger)
{
    public async Task SweepAsync(CancellationToken ct = default)
    {
        var alerts = await db.Alerts
            .Where(a => a.Type == AlertType.Technical && a.Status == AlertStatus.Active)
            .ToListAsync(ct);
        if (alerts.Count == 0) return;

        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = to.AddDays(-180);
        var bySymbol = alerts.GroupBy(a => a.Symbol, StringComparer.OrdinalIgnoreCase);
        foreach (var grp in bySymbol)
        {
            IReadOnlyList<OhlcBarDto> bars;
            try
            {
                bars = await provider.GetOhlcAsync(grp.Key, from, to, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Bar fetch failed for {Symbol}", grp.Key);
                continue;
            }
            if (bars.Count < 2) continue;
            var closes = bars.Select(b => b.Close).ToList();
            var last = closes[^1];
            foreach (var a in grp)
            {
                var period = a.IndicatorPeriod ?? 14;
                var msg = (string?)null;
                decimal? value = null;
                switch (a.Condition)
                {
                    case AlertCondition.SmaCrossAbove:
                        if (indicators.CrossedAboveSma(closes, period))
                        { value = last; msg = $"{a.Symbol} closed above {period}d SMA at {last:0.##}"; }
                        break;
                    case AlertCondition.SmaCrossBelow:
                        if (indicators.CrossedBelowSma(closes, period))
                        { value = last; msg = $"{a.Symbol} closed below {period}d SMA at {last:0.##}"; }
                        break;
                    case AlertCondition.RsiAbove:
                        var rsiA = indicators.Rsi(closes, period).LastOrDefault();
                        if (rsiA is decimal r1 && r1 >= a.Threshold)
                        { value = r1; msg = $"{a.Symbol} {period}d RSI {r1:0.##} ≥ {a.Threshold}"; }
                        break;
                    case AlertCondition.RsiBelow:
                        var rsiB = indicators.Rsi(closes, period).LastOrDefault();
                        if (rsiB is decimal r2 && r2 <= a.Threshold)
                        { value = r2; msg = $"{a.Symbol} {period}d RSI {r2:0.##} ≤ {a.Threshold}"; }
                        break;
                }
                if (msg is not null)
                    await dispatcher.TripAsync(a, value, msg, $"period={period}", ct);
            }
        }
    }
}

/// <summary>M10 #48 — fire N days before each held ticker's next earnings event.</summary>
public sealed class EarningsAlertEvaluator(StockyDbContext db, AlertDispatcher dispatcher)
{
    public async Task SweepAsync(CancellationToken ct = default)
    {
        var alerts = await db.Alerts
            .Where(a => a.Type == AlertType.Earnings && a.Status == AlertStatus.Active)
            .ToListAsync(ct);
        if (alerts.Count == 0) return;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var a in alerts)
        {
            var window = a.DaysBeforeEarnings ?? (int)a.Threshold;
            if (window <= 0) window = 7;
            var until = today.AddDays(window);
            var next = await db.EarningsEvents
                .Where(e => e.Symbol == a.Symbol && e.Date >= today && e.Date <= until)
                .OrderBy(e => e.Date)
                .FirstOrDefaultAsync(ct);
            if (next is null) continue;
            var daysOut = next.Date.DayNumber - today.DayNumber;
            var msg = $"{a.Symbol} reports earnings in {daysOut} day(s) ({next.Date:yyyy-MM-dd})";
            await dispatcher.TripAsync(a, daysOut, msg, $"earnings:{next.Date:yyyy-MM-dd}", ct);
        }
    }
}

/// <summary>
/// M10 #49 — naive keyword + sentiment scan over recent NewsItem rows.
/// Sentiment is derived from a small lexicon so the test suite is stable;
/// a real impl would call out to an LLM / classifier.
/// </summary>
public sealed class NewsAlertEvaluator(StockyDbContext db, AlertDispatcher dispatcher)
{
    private static readonly string[] PosWords = { "beat", "beats", "surge", "surges", "record", "growth", "upgrade", "outperform", "buy" };
    private static readonly string[] NegWords = { "miss", "misses", "downgrade", "lawsuit", "probe", "decline", "loss", "fraud", "sell" };

    public static decimal Score(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0m;
        var t = text.ToLowerInvariant();
        var pos = PosWords.Count(w => t.Contains(w));
        var neg = NegWords.Count(w => t.Contains(w));
        if (pos + neg == 0) return 0m;
        return Math.Round((decimal)(pos - neg) / (pos + neg), 4);
    }

    public async Task SweepAsync(CancellationToken ct = default)
    {
        var alerts = await db.Alerts
            .Where(a => a.Type == AlertType.News && a.Status == AlertStatus.Active)
            .ToListAsync(ct);
        if (alerts.Count == 0) return;

        var since = DateTimeOffset.UtcNow.AddHours(-24);
        var news = await db.NewsItems
            .Where(n => n.PublishedAt >= since)
            .ToListAsync(ct);
        if (news.Count == 0) return;

        foreach (var a in alerts)
        {
            var keyword = (a.KeywordFilter ?? string.Empty).ToLowerInvariant();
            var minSent = a.MinSentiment;
            foreach (var item in news)
            {
                if (!string.IsNullOrWhiteSpace(a.Symbol) && !string.Equals(item.Symbol, a.Symbol, StringComparison.OrdinalIgnoreCase))
                    continue;
                var hay = ((item.Headline ?? string.Empty) + " " + (item.Summary ?? string.Empty)).ToLowerInvariant();
                if (keyword.Length > 0 && !hay.Contains(keyword)) continue;
                var score = Score(hay);
                if (minSent.HasValue && score < minSent.Value) continue;
                if (minSent is null && keyword.Length == 0) continue; // require at least one filter
                var msg = $"News match for {a.Symbol}: {item.Headline} (sent {score:+0.00;-0.00;0.00})";
                await dispatcher.TripAsync(a, score, msg, $"news:{item.Id}", ct);
                break; // one trip per sweep
            }
        }
    }
}

/// <summary>
/// M10 #50 — fires when any holding in the portfolio has drifted further than
/// <c>Threshold</c> percentage points away from its target weight.
/// </summary>
public sealed class DriftAlertEvaluator(StockyDbContext db, RebalanceService rebalance, AlertDispatcher dispatcher)
{
    public async Task SweepAsync(CancellationToken ct = default)
    {
        var alerts = await db.Alerts
            .Where(a => a.Type == AlertType.Drift && a.Status == AlertStatus.Active && a.PortfolioId != null)
            .ToListAsync(ct);
        foreach (var a in alerts)
        {
            var report = await rebalance.ComputeAsync(a.PortfolioId!.Value, a.OwnerId, ct);
            if (report is null) continue;
            var worst = report.Suggestions
                .Where(s => s.TargetWeightPercent > 0)
                .OrderByDescending(s => Math.Abs(s.DriftPercent))
                .FirstOrDefault();
            if (worst is null) continue;
            if (Math.Abs(worst.DriftPercent) < a.Threshold) continue;
            var msg = $"{worst.Symbol} drifted {worst.DriftPercent:+0.##;-0.##}pp from target ({worst.TargetWeightPercent:0.##}%)";
            await dispatcher.TripAsync(a, worst.DriftPercent, msg, $"drift:{worst.Symbol}", ct);
        }
    }
}

/// <summary>
/// M10 #51 — surfaces insider clusters: ≥ <c>Threshold</c> trades of one type
/// (Buy or Sell) within the last 30 days for the alert's symbol.
/// </summary>
public sealed class InsiderAlertEvaluator(StockyDbContext db, IInsiderTradeProvider provider, AlertDispatcher dispatcher)
{
    public async Task SweepAsync(CancellationToken ct = default)
    {
        var alerts = await db.Alerts
            .Where(a => a.Type == AlertType.Insider && a.Status == AlertStatus.Active)
            .ToListAsync(ct);
        foreach (var a in alerts)
        {
            var trades = await provider.GetRecentTradesAsync(a.Symbol, 30, ct);
            var wantBuy = a.Condition == AlertCondition.InsiderClusterBuy;
            var matching = trades
                .Where(t => string.Equals(t.TransactionType, wantBuy ? "Buy" : "Sell", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matching.Count < a.Threshold) continue;
            var net = matching.Sum(t => t.Shares) * (wantBuy ? 1m : -1m);
            var msg = $"{a.Symbol}: {matching.Count} insider {(wantBuy ? "buys" : "sells")} in 30d (net {net:0} shares)";
            await dispatcher.TripAsync(a, matching.Count, msg, $"insider:{(wantBuy ? "buy" : "sell")}", ct);
        }
    }
}

/// <summary>
/// Periodic sweeper that wakes each non-price evaluator. Price + technical
/// run on the existing quote-refresh tick; this job covers slow-moving
/// signals (earnings, news, drift, insider clusters).
/// </summary>
public sealed class AlertSweepJob(IServiceProvider services, IConfiguration config, ILogger<AlertSweepJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = config.GetValue("Alerts:SweepSeconds", 60);
        var delay = TimeSpan.FromSeconds(Math.Max(seconds, 15));
        try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); } catch { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = services.CreateScope();
                var sp = scope.ServiceProvider;
                await sp.GetRequiredService<TechnicalAlertEvaluator>().SweepAsync(stoppingToken);
                await sp.GetRequiredService<EarningsAlertEvaluator>().SweepAsync(stoppingToken);
                await sp.GetRequiredService<NewsAlertEvaluator>().SweepAsync(stoppingToken);
                await sp.GetRequiredService<DriftAlertEvaluator>().SweepAsync(stoppingToken);
                await sp.GetRequiredService<InsiderAlertEvaluator>().SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Alert sweep iteration failed");
            }
            try { await Task.Delay(delay, stoppingToken); } catch { break; }
        }
    }
}
