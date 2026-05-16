using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;
using Stocky.Api.Services;
using Xunit;

namespace Stocky.Api.Tests;

/// <summary>M10 — Advanced Alerts unit tests covering indicators, evaluators, dispatcher, and history.</summary>
public class M10AlertsTests
{
    private static StockyDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<StockyDbContext>()
            .UseInMemoryDatabase($"m10-{Guid.NewGuid()}")
            .Options;
        return new StockyDbContext(options);
    }

    private static AlertDispatcher NewDispatcher(StockyDbContext db) =>
        new(db, new IAlertChannel[] { new InboxChannel() }, NullLogger<AlertDispatcher>.Instance);

    // -------- Indicators --------------------------------------------------

    [Fact]
    public void Sma_returns_rolling_average_for_known_series()
    {
        var svc = new TechnicalIndicatorService();
        var closes = new decimal[] { 1, 2, 3, 4, 5, 6 };
        var sma = svc.Sma(closes, 3);
        Assert.Null(sma[0]);
        Assert.Null(sma[1]);
        Assert.Equal(2m, sma[2]);
        Assert.Equal(3m, sma[3]);
        Assert.Equal(5m, sma[5]);
    }

    [Fact]
    public void Rsi_is_bounded_and_high_on_monotonic_uptrend()
    {
        var svc = new TechnicalIndicatorService();
        var closes = Enumerable.Range(1, 30).Select(i => (decimal)i).ToList();
        var rsi = svc.Rsi(closes, 14);
        var last = rsi[^1];
        Assert.NotNull(last);
        Assert.InRange(last!.Value, 0m, 100m);
        Assert.True(last.Value > 70m, $"expected RSI>70 on monotonic uptrend, got {last}");
    }

    [Fact]
    public void Sma_cross_detects_only_the_crossing_bar()
    {
        var svc = new TechnicalIndicatorService();
        // Flat then jump above to force a cross on the last bar.
        var closes = new decimal[] { 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 20 };
        Assert.True(svc.CrossedAboveSma(closes, 5));
        Assert.False(svc.CrossedBelowSma(closes, 5));
    }

    // -------- Dispatcher --------------------------------------------------

    [Fact]
    public async Task Dispatcher_writes_history_event_and_marks_alert_triggered()
    {
        using var db = NewDb();
        var alert = new Alert { OwnerId = "u1", Symbol = "AAPL", Condition = AlertCondition.PriceAbove, Threshold = 100m, Channels = "Inbox" };
        db.Alerts.Add(alert);
        await db.SaveChangesAsync();
        await NewDispatcher(db).TripAsync(alert, 101m, "fired", null, default);
        Assert.Equal(AlertStatus.Triggered, alert.Status);
        var ev = await db.AlertEvents.SingleAsync();
        Assert.Equal(alert.Id, ev.AlertId);
        Assert.Contains("Inbox", ev.Channels);
        Assert.Equal(101m, ev.TriggeredValue);
    }

    [Fact]
    public async Task Dispatcher_skips_snoozed_alert()
    {
        using var db = NewDb();
        var alert = new Alert
        {
            OwnerId = "u1", Symbol = "AAPL", Condition = AlertCondition.PriceAbove, Threshold = 100m,
            Channels = "Inbox", SnoozedUntil = DateTimeOffset.UtcNow.AddHours(1)
        };
        db.Alerts.Add(alert);
        await db.SaveChangesAsync();
        await NewDispatcher(db).TripAsync(alert, 101m, "fired", null, default);
        Assert.Equal(AlertStatus.Active, alert.Status);
        Assert.Empty(db.AlertEvents);
    }

    // -------- Evaluators --------------------------------------------------

    [Fact]
    public async Task EarningsAlertEvaluator_trips_when_event_in_window()
    {
        using var db = NewDb();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        db.EarningsEvents.Add(new EarningsEvent { Symbol = "AAPL", Date = today.AddDays(3) });
        var alert = new Alert
        {
            OwnerId = "u1", Symbol = "AAPL", Type = AlertType.Earnings,
            Condition = AlertCondition.EarningsWithinDays, Threshold = 7, DaysBeforeEarnings = 7,
            Channels = "Inbox"
        };
        db.Alerts.Add(alert);
        await db.SaveChangesAsync();
        await new EarningsAlertEvaluator(db, NewDispatcher(db)).SweepAsync();
        Assert.Equal(AlertStatus.Triggered, alert.Status);
        Assert.Single(db.AlertEvents);
    }

    [Fact]
    public async Task NewsAlertEvaluator_matches_keyword_and_sentiment()
    {
        using var db = NewDb();
        db.NewsItems.Add(new NewsItem
        {
            Symbol = "AAPL",
            Headline = "AAPL beats earnings and surges to record",
            Summary = "growth was strong",
            Source = "Stub",
            PublishedAt = DateTimeOffset.UtcNow.AddMinutes(-30)
        });
        var alert = new Alert
        {
            OwnerId = "u1", Symbol = "AAPL", Type = AlertType.News,
            Condition = AlertCondition.NewsKeyword, Threshold = 0m,
            KeywordFilter = "earnings", MinSentiment = 0.2m, Channels = "Inbox"
        };
        db.Alerts.Add(alert);
        await db.SaveChangesAsync();
        await new NewsAlertEvaluator(db, NewDispatcher(db)).SweepAsync();
        Assert.Equal(AlertStatus.Triggered, alert.Status);
        var ev = await db.AlertEvents.SingleAsync();
        Assert.Contains("News match", ev.Message);
    }

    [Fact]
    public async Task InsiderAlertEvaluator_trips_on_cluster_threshold()
    {
        using var db = NewDb();
        var sym = "AAPL";
        for (var i = 0; i < 4; i++)
        {
            db.InsiderTrades.Add(new InsiderTrade
            {
                Symbol = sym, InsiderName = $"X{i}", TransactionType = "Buy",
                Shares = 1000, Price = 100, FiledAt = DateTimeOffset.UtcNow.AddDays(-i)
            });
        }
        var alert = new Alert
        {
            OwnerId = "u1", Symbol = sym, Type = AlertType.Insider,
            Condition = AlertCondition.InsiderClusterBuy, Threshold = 3, Channels = "Inbox"
        };
        db.Alerts.Add(alert);
        await db.SaveChangesAsync();
        var eval = new InsiderAlertEvaluator(db, new StubInsiderTradeProvider(db), NewDispatcher(db));
        await eval.SweepAsync();
        Assert.Equal(AlertStatus.Triggered, alert.Status);
    }

    [Fact]
    public async Task AlertHistory_endpoint_returns_events_desc()
    {
        using var db = NewDb();
        var owner = "00000000-0000-0000-0000-000000000001"; // dev bypass oid
        var a = new Alert { OwnerId = owner, Symbol = "AAPL", Condition = AlertCondition.PriceAbove, Threshold = 100m, Channels = "Inbox" };
        db.Alerts.Add(a);
        await db.SaveChangesAsync();
        var disp = NewDispatcher(db);
        await disp.TripAsync(a, 101m, "first", null, default);
        a.Status = AlertStatus.Active;
        await disp.TripAsync(a, 102m, "second", null, default);

        var ctrl = new Stocky.Api.Controllers.AlertsController(db);
        ctrl.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]
                {
                    new System.Security.Claims.Claim("oid", owner)
                }, "Test"))
            }
        };
        var res = (await ctrl.History(200)).Result as Microsoft.AspNetCore.Mvc.OkObjectResult;
        var list = Assert.IsAssignableFrom<IEnumerable<AlertEventDto>>(res!.Value).ToList();
        Assert.Equal(2, list.Count);
        Assert.True(list[0].TriggeredAt >= list[1].TriggeredAt);
        Assert.Equal("second", list[0].Message);
    }
}
