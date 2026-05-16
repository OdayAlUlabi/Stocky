using Stocky.Api.Dtos;

namespace Stocky.Api.Services;

/// <summary>
/// Deterministic stub: derives a "current" price from a symbol hash and the
/// day-of-year so the UI shows believable, but offline, quotes. Lets the
/// dashboard / position detail / alerts run without external dependencies.
/// </summary>
public sealed class StubMarketDataProvider : IMarketDataProvider
{
    private static readonly string[] NewsHeadlines =
    [
        "Markets close mixed as tech leads gains",
        "Fed signals patience on rate cuts",
        "Oil rallies on supply concerns",
        "Crypto rebounds after midweek dip",
        "Earnings beat lifts megacap names",
        "Treasury yields ease ahead of CPI",
        "Retail sales surprise to the upside",
        "Chipmakers extend rally on AI demand"
    ];

    public Task<IReadOnlyList<QuoteDto>> GetQuotesAsync(IReadOnlyCollection<string> symbols, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var quotes = symbols.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(s =>
            {
                var (price, change, pct) = Synthesize(s, now);
                return new QuoteDto(s.ToUpperInvariant(), price, change, pct, now);
            })
            .ToList();
        return Task.FromResult<IReadOnlyList<QuoteDto>>(quotes);
    }

    public Task<IReadOnlyList<NewsItemDto>> GetNewsAsync(IReadOnlyCollection<string>? symbols, int limit, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var list = new List<NewsItemDto>();
        var pool = symbols is { Count: > 0 } ? symbols.ToArray() : new[] { (string?)null };
        var rng = new Random(now.DayOfYear);
        for (int i = 0; i < limit; i++)
        {
            var sym = pool[i % pool.Length];
            var head = NewsHeadlines[rng.Next(NewsHeadlines.Length)];
            list.Add(new NewsItemDto(
                i + 1,
                sym is null ? head : $"{sym}: {head}",
                "Auto-generated headline from local stub provider.",
                "StockyWire",
                null,
                sym,
                now.AddMinutes(-i * 17),
                sym is null ? "General" : "Symbol"));
        }
        return Task.FromResult<IReadOnlyList<NewsItemDto>>(list);
    }

    public Task<IReadOnlyList<EarningsEventDto>> GetEarningsAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var picks = new[] { "AAPL", "MSFT", "NVDA", "GOOGL", "AMZN", "META", "TSLA", "JPM" };
        var list = new List<EarningsEventDto>();
        long id = 1;
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday) continue;
            var sym = picks[(d.DayNumber + picks.Length) % picks.Length];
            list.Add(new EarningsEventDto(id++, sym, d, "AMC", 1.20m + (d.DayNumber % 5) * 0.05m, null, 50_000_000_000m, null));
        }
        return Task.FromResult<IReadOnlyList<EarningsEventDto>>(list);
    }

    public Task<IReadOnlyDictionary<string, IReadOnlyList<DailyBarDto>>> GetDailyBarsAsync(
        IReadOnlyCollection<string> symbols, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        // Stub has no historical data: callers should carry forward the last
        // known price (cost basis from transactions usually). Returning an
        // empty dictionary keeps the history endpoint working offline.
        IReadOnlyDictionary<string, IReadOnlyList<DailyBarDto>> empty =
            new Dictionary<string, IReadOnlyList<DailyBarDto>>();
        return Task.FromResult(empty);
    }

    private static (decimal Price, decimal Change, decimal ChangePct) Synthesize(string symbol, DateTimeOffset now)
    {
        var hash = 0;
        foreach (var c in symbol.ToUpperInvariant()) hash = unchecked(hash * 31 + c);
        var basePrice = 25m + Math.Abs(hash % 400);
        var daySeed = (decimal)Math.Sin((now.DayOfYear + hash) * 0.37) * 6m;
        var price = Math.Round(basePrice + daySeed, 2);
        if (price < 1m) price = 1m;
        var change = Math.Round(daySeed * 0.25m, 2);
        var pct = price == 0 ? 0 : Math.Round(change / price * 100m, 2);
        return (price, change, pct);
    }
}
