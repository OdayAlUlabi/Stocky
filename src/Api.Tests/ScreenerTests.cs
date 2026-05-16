using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Controllers;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;
using Xunit;

namespace Stocky.Api.Tests;

public class ScreenerTests
{
    private static StockyDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<StockyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new StockyDbContext(opts);
    }

    private static async Task SeedAsync(StockyDbContext db)
    {
        db.Instruments.AddRange(
            new Instrument { Symbol = "AAPL", Name = "Apple Inc.",         Exchange = "NASDAQ", Currency = "USD", AssetClass = "Equity" },
            new Instrument { Symbol = "MSFT", Name = "Microsoft Corp.",    Exchange = "NASDAQ", Currency = "USD", AssetClass = "Equity" },
            new Instrument { Symbol = "NVDA", Name = "NVIDIA Corp.",       Exchange = "NASDAQ", Currency = "USD", AssetClass = "Equity" },
            new Instrument { Symbol = "JPM",  Name = "JPMorgan Chase",     Exchange = "NYSE",   Currency = "USD", AssetClass = "Equity" },
            new Instrument { Symbol = "SPY",  Name = "SPDR S&P 500 ETF",   Exchange = "NYSE",   Currency = "USD", AssetClass = "ETF"    });

        db.InstrumentMetadata.AddRange(
            new InstrumentMetadata { Symbol = "AAPL", Sector = "Technology",        Country = "US", MarketCap = 3_000_000_000_000m, Beta = 1.20m, DividendYield = 0.005m },
            new InstrumentMetadata { Symbol = "MSFT", Sector = "Technology",        Country = "US", MarketCap = 3_100_000_000_000m, Beta = 0.95m, DividendYield = 0.008m },
            new InstrumentMetadata { Symbol = "NVDA", Sector = "Technology",        Country = "US", MarketCap = 2_500_000_000_000m, Beta = 1.80m, DividendYield = 0.001m },
            new InstrumentMetadata { Symbol = "JPM",  Sector = "Financial Services",Country = "US", MarketCap =   500_000_000_000m, Beta = 1.10m, DividendYield = 0.025m });
        // SPY has no metadata row on purpose to test left-join behaviour.

        var now = DateTimeOffset.UtcNow;
        db.PriceQuotes.AddRange(
            new PriceQuote { Symbol = "AAPL", Price = 200m, AsOf = now },
            new PriceQuote { Symbol = "MSFT", Price = 420m, AsOf = now },
            new PriceQuote { Symbol = "NVDA", Price = 950m, AsOf = now },
            new PriceQuote { Symbol = "JPM",  Price = 210m, AsOf = now },
            new PriceQuote { Symbol = "SPY",  Price = 525m, AsOf = now });

        await db.SaveChangesAsync();
    }

    private static ScreenerResultDto Unwrap(ActionResult<ScreenerResultDto> result)
    {
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<ScreenerResultDto>(ok.Value);
    }

    [Fact]
    public async Task Screener_NoFilters_ReturnsAllRowsSortedByMarketCapDesc()
    {
        using var db = CreateDb();
        await SeedAsync(db);
        var ctrl = new SecuritiesController(db);

        var res = Unwrap(await ctrl.Screener(null, null, null, null, null, null, null, null));

        Assert.Equal(5, res.Total);
        // MSFT (3.1T) first, then AAPL (3T), NVDA (2.5T), JPM (500B), SPY (no metadata → 0)
        Assert.Equal(new[] { "MSFT", "AAPL", "NVDA", "JPM", "SPY" }, res.Rows.Select(r => r.Symbol).ToArray());
        // Latest price flows through.
        Assert.Equal(420m, res.Rows[0].LatestPrice);
        // Left join surfaces nulls for SPY metadata.
        var spy = res.Rows.Single(r => r.Symbol == "SPY");
        Assert.Null(spy.Sector);
        Assert.Null(spy.MarketCap);
        Assert.Equal(525m, spy.LatestPrice);
    }

    [Fact]
    public async Task Screener_SectorFilter_RestrictsResults()
    {
        using var db = CreateDb();
        await SeedAsync(db);
        var ctrl = new SecuritiesController(db);

        var res = Unwrap(await ctrl.Screener(null, null, "Financial Services", null, null, null, null, null));

        Assert.Equal(1, res.Total);
        Assert.Equal("JPM", res.Rows.Single().Symbol);
    }

    [Fact]
    public async Task Screener_MinDividendYieldAndMaxBeta_BothApplied()
    {
        using var db = CreateDb();
        await SeedAsync(db);
        var ctrl = new SecuritiesController(db);

        // Want dividend yield ≥ 0.5% AND beta ≤ 1.5 → excludes NVDA (beta 1.8) and JPM is 1.10 with 2.5% yield, MSFT 0.95/0.8%, AAPL 1.2/0.5%.
        var res = Unwrap(await ctrl.Screener(null, null, null, null, null, null, 0.005m, 1.5m));

        var symbols = res.Rows.Select(r => r.Symbol).OrderBy(s => s).ToArray();
        Assert.Equal(new[] { "AAPL", "JPM", "MSFT" }, symbols);
        Assert.DoesNotContain(res.Rows, r => r.Symbol == "NVDA");
    }

    [Fact]
    public async Task Screener_SearchQuery_MatchesSymbolOrName()
    {
        using var db = CreateDb();
        await SeedAsync(db);
        var ctrl = new SecuritiesController(db);

        var res = Unwrap(await ctrl.Screener("Apple", null, null, null, null, null, null, null));

        Assert.Single(res.Rows);
        Assert.Equal("AAPL", res.Rows[0].Symbol);
    }

    [Fact]
    public async Task Screener_SortDividendYieldDesc_PlacesJpmFirst()
    {
        using var db = CreateDb();
        await SeedAsync(db);
        var ctrl = new SecuritiesController(db);

        var res = Unwrap(await ctrl.Screener(null, null, null, null, null, null, null, null, sort: "divyield-desc"));

        Assert.Equal("JPM", res.Rows[0].Symbol);
    }

    [Fact]
    public async Task Screener_AssetClassFilter_KeepsOnlyEtf()
    {
        using var db = CreateDb();
        await SeedAsync(db);
        var ctrl = new SecuritiesController(db);

        var res = Unwrap(await ctrl.Screener(null, "ETF", null, null, null, null, null, null));

        Assert.Single(res.Rows);
        Assert.Equal("SPY", res.Rows[0].Symbol);
    }
}
