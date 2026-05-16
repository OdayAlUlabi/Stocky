using Stocky.Api.Dtos;

namespace Stocky.Api.Services;

/// <summary>
/// Deterministic stub for the extended market-data surface. Derives values
/// from a symbol-hash + day-of-year so the UI shows believable, but offline,
/// data for L2 book, extended-hours quotes, filings, insider trades, short
/// interest, the economic calendar, and options flow.
/// </summary>
public sealed class StubExtendedMarketDataProvider(IMarketDataProvider quotes) : IExtendedMarketDataProvider
{
    private static int Hash(string s)
    {
        var h = 0;
        foreach (var c in s.ToUpperInvariant()) h = unchecked(h * 31 + c);
        return Math.Abs(h);
    }

    public async Task<OrderBookDto> GetOrderBookAsync(string symbol, int depth, CancellationToken ct = default)
    {
        symbol = symbol.ToUpperInvariant();
        depth = Math.Clamp(depth, 1, 25);
        var q = (await quotes.GetQuotesAsync(new[] { symbol }, ct))[0];
        var mid = q.Price;
        var tick = Math.Max(Math.Round(mid * 0.0005m, 2), 0.01m);
        var rng = new Random(Hash(symbol) + DateTime.UtcNow.DayOfYear);
        var bids = new List<OrderBookLevelDto>(depth);
        var asks = new List<OrderBookLevelDto>(depth);
        for (var i = 0; i < depth; i++)
        {
            bids.Add(new OrderBookLevelDto(Math.Round(mid - tick * (i + 1), 2), 100 * rng.Next(1, 40)));
            asks.Add(new OrderBookLevelDto(Math.Round(mid + tick * (i + 1), 2), 100 * rng.Next(1, 40)));
        }
        return new OrderBookDto(symbol, bids, asks, DateTimeOffset.UtcNow);
    }

    public async Task<ExtendedQuoteDto> GetExtendedQuoteAsync(string symbol, CancellationToken ct = default)
    {
        symbol = symbol.ToUpperInvariant();
        var q = (await quotes.GetQuotesAsync(new[] { symbol }, ct))[0];
        var et = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
            TryEastern() ?? TimeZoneInfo.Utc);
        var minutes = et.Hour * 60 + et.Minute;
        string session;
        if (et.DayOfWeek == DayOfWeek.Saturday || et.DayOfWeek == DayOfWeek.Sunday) session = "Closed";
        else if (minutes >= 4 * 60 && minutes < 9 * 60 + 30) session = "PreMarket";
        else if (minutes >= 9 * 60 + 30 && minutes < 16 * 60) session = "Regular";
        else if (minutes >= 16 * 60 && minutes < 20 * 60) session = "AfterHours";
        else session = "Closed";

        var drift = (decimal)Math.Sin((Hash(symbol) + DateTimeOffset.UtcNow.Hour) * 0.41) * 0.6m;
        var extPrice = Math.Round(q.Price + drift, 2);
        var extChange = Math.Round(extPrice - q.Price, 2);
        var extPct = q.Price == 0 ? 0 : Math.Round(extChange / q.Price * 100m, 2);
        return new ExtendedQuoteDto(symbol, q.Price, extPrice, extChange, extPct, session, DateTimeOffset.UtcNow);
    }

    private static TimeZoneInfo? TryEastern()
    {
        foreach (var id in new[] { "America/New_York", "Eastern Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { }
        }
        return null;
    }

    public Task<IReadOnlyList<FilingDto>> GetFilingsAsync(IReadOnlyCollection<string> symbols, int limit, CancellationToken ct = default)
    {
        var forms = new[] { "10-K", "10-Q", "8-K", "4", "S-1", "DEF 14A" };
        var titles = new[]
        {
            "Annual report", "Quarterly report", "Current report (material event)",
            "Statement of changes in beneficial ownership", "Registration statement",
            "Proxy statement"
        };
        var list = new List<FilingDto>();
        long id = 1;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var sym in symbols.Select(s => s.ToUpperInvariant()).Distinct())
        {
            var rng = new Random(Hash(sym));
            for (var i = 0; i < Math.Min(limit, 5); i++)
            {
                var idx = rng.Next(forms.Length);
                var filed = today.AddDays(-rng.Next(1, 200));
                var acc = $"{rng.Next(1000000, 9999999):D7}-{rng.Next(10, 99):D2}-{rng.Next(100000, 999999):D6}";
                list.Add(new FilingDto(id++, sym, forms[idx], titles[idx], filed,
                    $"https://www.sec.gov/cgi-bin/browse-edgar?action=getcompany&CIK={sym}&type={forms[idx]}",
                    acc));
            }
        }
        return Task.FromResult<IReadOnlyList<FilingDto>>(
            list.OrderByDescending(f => f.FiledAt).Take(limit).ToList());
    }

    public Task<IReadOnlyList<InsiderTradeDto>> GetInsiderTradesAsync(string symbol, int limit, CancellationToken ct = default)
    {
        symbol = symbol.ToUpperInvariant();
        var insiders = new[]
        {
            ("Jane Smith", "CEO"), ("Mark Lee", "CFO"), ("Priya Singh", "Director"),
            ("Sam Patel", "VP Engineering"), ("Akira Tanaka", "Director"), ("Lisa Rojas", "COO")
        };
        var sides = new[] { "Buy", "Sell" };
        var rng = new Random(Hash(symbol) + 7);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var list = new List<InsiderTradeDto>();
        for (var i = 0; i < limit; i++)
        {
            var ins = insiders[rng.Next(insiders.Length)];
            var qty = rng.Next(500, 25_000);
            var price = Math.Round(25m + rng.Next(50, 400) + (decimal)rng.NextDouble(), 2);
            list.Add(new InsiderTradeDto(i + 1, symbol, ins.Item1, ins.Item2,
                sides[rng.Next(2)], qty, price, qty * price,
                today.AddDays(-rng.Next(1, 120))));
        }
        return Task.FromResult<IReadOnlyList<InsiderTradeDto>>(list);
    }

    public Task<ShortInterestDto> GetShortInterestAsync(string symbol, CancellationToken ct = default)
    {
        symbol = symbol.ToUpperInvariant();
        var rng = new Random(Hash(symbol) + 13);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var history = new List<ShortInterestPointDto>(12);
        decimal floatShares = 100_000_000m + rng.Next(0, 900_000_000);
        for (var i = 11; i >= 0; i--)
        {
            var date = today.AddDays(-i * 14);
            var pct = (decimal)(2 + rng.NextDouble() * 18); // 2-20%
            var shortInt = Math.Round(floatShares * pct / 100m, 0);
            var dtc = Math.Round((decimal)(0.5 + rng.NextDouble() * 8), 2);
            history.Add(new ShortInterestPointDto(date, shortInt, Math.Round(pct, 2), dtc));
        }
        var latest = history[^1];
        return Task.FromResult(new ShortInterestDto(symbol, latest.ReportDate, latest.ShortInterest,
            floatShares, latest.PercentOfFloat, latest.DaysToCover, history));
    }

    public Task<IReadOnlyList<EconomicEventDto>> GetEconomicCalendarAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var indicators = new (string Name, string Country, string Importance)[]
        {
            ("CPI YoY", "US", "High"),
            ("Non-Farm Payrolls", "US", "High"),
            ("Unemployment Rate", "US", "High"),
            ("FOMC Rate Decision", "US", "High"),
            ("Retail Sales MoM", "US", "Medium"),
            ("ISM Manufacturing PMI", "US", "Medium"),
            ("Initial Jobless Claims", "US", "Medium"),
            ("GDP QoQ", "US", "High"),
            ("ECB Rate Decision", "EU", "High"),
            ("UK CPI YoY", "UK", "Medium"),
        };
        var list = new List<EconomicEventDto>();
        long id = 1;
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday) continue;
            var rng = new Random(d.DayNumber);
            var count = rng.Next(1, 4);
            for (var i = 0; i < count; i++)
            {
                var ind = indicators[rng.Next(indicators.Length)];
                var actual = Math.Round((decimal)(rng.NextDouble() * 5), 2);
                var forecast = Math.Round(actual + (decimal)(rng.NextDouble() - 0.5) * 0.4m, 2);
                var prev = Math.Round(forecast + (decimal)(rng.NextDouble() - 0.5) * 0.4m, 2);
                list.Add(new EconomicEventDto(id++, d,
                    $"{08 + i * 2:D2}:30", ind.Country, ind.Name, ind.Importance,
                    actual, forecast, prev, "%"));
            }
        }
        return Task.FromResult<IReadOnlyList<EconomicEventDto>>(list);
    }

    public async Task<OptionsFlowDto> GetOptionsFlowAsync(string symbol, int limit, CancellationToken ct = default)
    {
        symbol = symbol.ToUpperInvariant();
        var q = (await quotes.GetQuotesAsync(new[] { symbol }, ct))[0];
        var rng = new Random(Hash(symbol) + 19);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rows = new List<OptionsFlowRowDto>();
        for (var i = 0; i < limit; i++)
        {
            var side = rng.Next(2) == 0 ? "Call" : "Put";
            var strike = Math.Round(q.Price + (decimal)(rng.NextDouble() - 0.5) * q.Price * 0.2m, 2);
            var expiry = today.AddDays(rng.Next(7, 180));
            var volume = rng.Next(100, 20_000);
            var oi = rng.Next(50, 50_000);
            var premium = Math.Round(q.Price * 0.02m * (1 + (decimal)rng.NextDouble()), 2);
            var voiRatio = oi == 0 ? 0m : Math.Round((decimal)volume / oi, 2);
            rows.Add(new OptionsFlowRowDto(symbol, side, strike, expiry, volume, oi, voiRatio,
                premium, premium * volume * 100));
        }
        return new OptionsFlowDto(symbol, rows.OrderByDescending(r => r.VolumeOverOpenInterest).ToList(), DateTimeOffset.UtcNow);
    }
}
