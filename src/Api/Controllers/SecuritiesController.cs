using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Dtos;

namespace Stocky.Api.Controllers;

/// <summary>
/// Lightweight symbol search for the SCR-004 ticker autocomplete.
/// Backed by the local Instruments table plus a small static fallback so the
/// UI works before a market-data provider is wired in.
/// </summary>
[ApiController]
[Route("api/securities")]
public class SecuritiesController(StockyDbContext db) : ControllerBase
{
    private static readonly InstrumentDto[] Seed =
    [
        new("AAPL",  "Apple Inc.",                "NASDAQ", "USD", "Equity"),
        new("MSFT",  "Microsoft Corporation",     "NASDAQ", "USD", "Equity"),
        new("NVDA",  "NVIDIA Corporation",        "NASDAQ", "USD", "Equity"),
        new("GOOGL", "Alphabet Inc. Class A",     "NASDAQ", "USD", "Equity"),
        new("AMZN",  "Amazon.com, Inc.",          "NASDAQ", "USD", "Equity"),
        new("META",  "Meta Platforms, Inc.",      "NASDAQ", "USD", "Equity"),
        new("TSLA",  "Tesla, Inc.",               "NASDAQ", "USD", "Equity"),
        new("BRK.B", "Berkshire Hathaway Inc.",   "NYSE",   "USD", "Equity"),
        new("JPM",   "JPMorgan Chase & Co.",      "NYSE",   "USD", "Equity"),
        new("V",     "Visa Inc.",                 "NYSE",   "USD", "Equity"),
        new("UNH",   "UnitedHealth Group",        "NYSE",   "USD", "Equity"),
        new("XOM",   "Exxon Mobil Corporation",   "NYSE",   "USD", "Equity"),
        new("INTC",  "Intel Corporation",         "NASDAQ", "USD", "Equity"),
        new("SPY",   "SPDR S&P 500 ETF Trust",    "NYSE",   "USD", "ETF"),
        new("QQQ",   "Invesco QQQ Trust",         "NASDAQ", "USD", "ETF"),
        new("VTI",   "Vanguard Total Stock Mkt",  "NYSE",   "USD", "ETF"),
        new("BTC",   "Bitcoin",                   "CRYPTO", "USD", "Crypto"),
        new("ETH",   "Ethereum",                  "CRYPTO", "USD", "Crypto"),
    ];

    [HttpGet("search")]
    public async Task<ActionResult<IEnumerable<InstrumentDto>>> Search([FromQuery] string? q, [FromQuery] int limit = 10)
    {
        if (limit <= 0 || limit > 50) limit = 10;
        var needle = (q ?? string.Empty).Trim();

        var local = await db.Instruments
            .Where(i => needle == "" || i.Symbol.Contains(needle) || i.Name.Contains(needle))
            .OrderBy(i => i.Symbol)
            .Take(limit)
            .Select(i => new InstrumentDto(i.Symbol, i.Name, i.Exchange, i.Currency, i.AssetClass))
            .ToListAsync();

        if (local.Count >= limit) return Ok(local);

        var localSymbols = local.Select(i => i.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seed = Seed.Where(s =>
                needle == "" ||
                s.Symbol.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                s.Name.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .Where(s => !localSymbols.Contains(s.Symbol))
            .Take(limit - local.Count);

        return Ok(local.Concat(seed).Take(limit));
    }

    /// <summary>
    /// Stock screener over the local Instruments + InstrumentMetadata + latest
    /// PriceQuotes. Filters are AND-combined; any null filter is ignored.
    /// Sort: marketcap-desc | marketcap-asc | divyield-desc | beta-asc | symbol.
    /// </summary>
    [HttpGet("screener")]
    public async Task<ActionResult<ScreenerResultDto>> Screener(
        [FromQuery] string? q,
        [FromQuery] string? assetClass,
        [FromQuery] string? sector,
        [FromQuery] string? country,
        [FromQuery] decimal? minMarketCap,
        [FromQuery] decimal? maxMarketCap,
        [FromQuery] decimal? minDividendYield,
        [FromQuery] decimal? maxBeta,
        [FromQuery] string? sort = "marketcap-desc",
        [FromQuery] int limit = 50)
    {
        if (limit <= 0 || limit > 200) limit = 50;
        var needle = (q ?? string.Empty).Trim();

        var query =
            from i in db.Instruments
            join m in db.InstrumentMetadata on i.Symbol equals m.Symbol into mm
            from m in mm.DefaultIfEmpty()
            select new { i, m };

        if (needle.Length > 0)
            query = query.Where(x => x.i.Symbol.Contains(needle) || x.i.Name.Contains(needle));
        if (!string.IsNullOrWhiteSpace(assetClass))
            query = query.Where(x => x.i.AssetClass == assetClass);
        if (!string.IsNullOrWhiteSpace(sector))
            query = query.Where(x => x.m != null && x.m.Sector == sector);
        if (!string.IsNullOrWhiteSpace(country))
            query = query.Where(x => x.m != null && x.m.Country == country);
        if (minMarketCap.HasValue)
            query = query.Where(x => x.m != null && x.m.MarketCap >= minMarketCap.Value);
        if (maxMarketCap.HasValue)
            query = query.Where(x => x.m != null && x.m.MarketCap <= maxMarketCap.Value);
        if (minDividendYield.HasValue)
            query = query.Where(x => x.m != null && x.m.DividendYield >= minDividendYield.Value);
        if (maxBeta.HasValue)
            query = query.Where(x => x.m != null && x.m.Beta <= maxBeta.Value);

        // Order before materialisation. EF-Core in-memory tolerates these.
        query = sort switch
        {
            "marketcap-asc"  => query.OrderBy(x => x.m == null ? 0m : x.m.MarketCap ?? 0m).ThenBy(x => x.i.Symbol),
            "divyield-desc"  => query.OrderByDescending(x => x.m == null ? 0m : x.m.DividendYield ?? 0m).ThenBy(x => x.i.Symbol),
            "beta-asc"       => query.OrderBy(x => x.m == null ? 999m : x.m.Beta ?? 999m).ThenBy(x => x.i.Symbol),
            "symbol"         => query.OrderBy(x => x.i.Symbol),
            _                => query.OrderByDescending(x => x.m == null ? 0m : x.m.MarketCap ?? 0m).ThenBy(x => x.i.Symbol),
        };

        var total = await query.CountAsync();
        var page = await query.Take(limit).ToListAsync();

        var symbols = page.Select(x => x.i.Symbol).ToList();
        var latest = await db.PriceQuotes
            .Where(p => symbols.Contains(p.Symbol))
            .GroupBy(p => p.Symbol)
            .Select(g => g.OrderByDescending(x => x.AsOf).First())
            .ToDictionaryAsync(p => p.Symbol, p => p.Price);

        var rows = page.Select(x => new ScreenerRowDto(
            x.i.Symbol,
            x.i.Name,
            x.i.AssetClass,
            x.m?.Sector,
            x.m?.Industry,
            x.m?.Country,
            x.m?.MarketCap,
            x.m?.Beta,
            x.m?.DividendYield,
            latest.TryGetValue(x.i.Symbol, out var px) ? px : (decimal?)null
        )).ToList();

        return Ok(new ScreenerResultDto(total, rows));
    }

    /// <summary>
    /// Returns the distinct facet values used by the screener filters.
    /// </summary>
    [HttpGet("screener/facets")]
    public async Task<ActionResult<object>> ScreenerFacets()
    {
        var assetClasses = await db.Instruments
            .Where(i => i.AssetClass != null && i.AssetClass != "")
            .Select(i => i.AssetClass).Distinct().OrderBy(s => s).ToListAsync();
        var sectors = await db.InstrumentMetadata
            .Where(m => m.Sector != null && m.Sector != "")
            .Select(m => m.Sector!).Distinct().OrderBy(s => s).ToListAsync();
        var countries = await db.InstrumentMetadata
            .Where(m => m.Country != null && m.Country != "")
            .Select(m => m.Country!).Distinct().OrderBy(s => s).ToListAsync();
        return Ok(new { assetClasses, sectors, countries });
    }
}
