using Microsoft.AspNetCore.Authorization;
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
[Authorize]
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
}
