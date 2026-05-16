using Microsoft.AspNetCore.Mvc;
using Stocky.Api.Controllers;
using Stocky.Api.Dtos;
using Stocky.Api.Services;
using Xunit;

namespace Stocky.Api.Tests;

/// <summary>M8 — controller smoke tests against the deterministic stub provider.</summary>
public class ExtendedMarketDataTests
{
    private static IExtendedMarketDataProvider NewProvider() =>
        new StubExtendedMarketDataProvider(new StubMarketDataProvider());

    [Fact]
    public async Task OrderBook_returns_requested_depth_with_bid_below_ask()
    {
        var ctrl = new QuotesMarketDataController(NewProvider());
        var res = (await ctrl.GetBook("AAPL", 10)).Result as OkObjectResult;
        var book = Assert.IsType<OrderBookDto>(res!.Value);
        Assert.Equal("AAPL", book.Symbol);
        Assert.Equal(10, book.Bids.Count);
        Assert.Equal(10, book.Asks.Count);
        Assert.True(book.Bids[0].Price < book.Asks[0].Price);
        Assert.True(book.Bids.All(l => l.Size > 0));
    }

    [Fact]
    public async Task OrderBook_clamps_depth_to_1_25()
    {
        var ctrl = new QuotesMarketDataController(NewProvider());
        var big = (await ctrl.GetBook("MSFT", 999)).Result as OkObjectResult;
        var book = Assert.IsType<OrderBookDto>(big!.Value);
        Assert.Equal(25, book.Bids.Count);
    }

    [Fact]
    public async Task Extended_quote_returns_valid_session_label()
    {
        var ctrl = new QuotesMarketDataController(NewProvider());
        var res = (await ctrl.GetExtended("AAPL")).Result as OkObjectResult;
        var q = Assert.IsType<ExtendedQuoteDto>(res!.Value);
        Assert.Contains(q.Session, new[] { "PreMarket", "Regular", "AfterHours", "Closed" });
        Assert.True(q.RegularPrice > 0);
        Assert.True(q.ExtendedPrice > 0);
    }

    [Fact]
    public async Task InsiderTrades_returns_requested_count()
    {
        var ctrl = new InsiderTradesController(NewProvider());
        var res = (await ctrl.Get("AAPL", 8)).Result as OkObjectResult;
        var list = Assert.IsAssignableFrom<IEnumerable<InsiderTradeDto>>(res!.Value);
        Assert.Equal(8, list.Count());
        Assert.All(list, t => Assert.True(t.Quantity > 0 && t.Price > 0));
        Assert.All(list, t => Assert.Contains(t.Side, new[] { "Buy", "Sell" }));
    }

    [Fact]
    public async Task ShortInterest_includes_history_and_latest_consistency()
    {
        var ctrl = new ShortInterestController(NewProvider());
        var res = (await ctrl.Get("NVDA")).Result as OkObjectResult;
        var data = Assert.IsType<ShortInterestDto>(res!.Value);
        Assert.NotEmpty(data.History);
        Assert.Equal(data.History[^1].ReportDate, data.ReportDate);
        Assert.True(data.PercentOfFloat is > 0 and < 100);
    }

    [Fact]
    public async Task EconomicCalendar_clamps_to_92_days()
    {
        var ctrl = new EconomicCalendarController(NewProvider());
        var from = new DateOnly(2025, 1, 1);
        var to = new DateOnly(2026, 1, 1); // > 92 days, should clamp
        var res = (await ctrl.Get(from, to)).Result as OkObjectResult;
        var list = Assert.IsAssignableFrom<IEnumerable<EconomicEventDto>>(res!.Value);
        Assert.NotEmpty(list);
        Assert.All(list, e => Assert.True(e.Date <= from.AddDays(92)));
        Assert.All(list, e => Assert.Contains(e.Importance, new[] { "High", "Medium", "Low" }));
    }

    [Fact]
    public async Task OptionsFlow_sorted_by_voi_ratio_desc()
    {
        var ctrl = new OptionsFlowController(NewProvider());
        var res = (await ctrl.Get("AAPL", 15)).Result as OkObjectResult;
        var data = Assert.IsType<OptionsFlowDto>(res!.Value);
        Assert.Equal(15, data.Rows.Count);
        for (var i = 1; i < data.Rows.Count; i++)
            Assert.True(data.Rows[i - 1].VolumeOverOpenInterest >= data.Rows[i].VolumeOverOpenInterest);
        Assert.All(data.Rows, r => Assert.Contains(r.Side, new[] { "Call", "Put" }));
    }

    [Fact]
    public async Task Filings_deterministic_for_same_symbol_set()
    {
        var p = NewProvider();
        var a = await p.GetFilingsAsync(new[] { "AAPL", "MSFT" }, 10);
        var b = await p.GetFilingsAsync(new[] { "AAPL", "MSFT" }, 10);
        Assert.Equal(a.Count, b.Count);
        Assert.NotEmpty(a);
        Assert.All(a, f => Assert.False(string.IsNullOrWhiteSpace(f.Url)));
    }
}
