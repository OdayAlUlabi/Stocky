using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;

namespace Stocky.Api.Services;

/// <summary>
/// Periodic refresher: pulls quotes for every symbol that appears in any
/// holding, watchlist, or transaction ledger, writes a new PriceQuote row,
/// and runs the alert evaluator. Interval comes from MarketData:RefreshSeconds
/// (default 5, minimum 5). Skips iterations when the US equity market is
/// closed unless MarketData:AlwaysRefresh=true.
/// </summary>
public sealed class QuoteRefresher(
    IServiceProvider services,
    IConfiguration config,
    ILogger<QuoteRefresher> logger) : BackgroundService
{
    private static readonly TimeZoneInfo EasternTz = ResolveEasternTimeZone();
    private bool _lastMarketOpenState = true;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = config.GetValue("MarketData:RefreshSeconds", 5);
        var delay = TimeSpan.FromSeconds(Math.Max(seconds, 5));
        var alwaysRefresh = config.GetValue("MarketData:AlwaysRefresh", false);

        // Small startup delay so the API can warm up first.
        try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); } catch { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (alwaysRefresh || IsUsEquityMarketOpen(DateTimeOffset.UtcNow))
                {
                    if (!_lastMarketOpenState)
                    {
                        logger.LogInformation("US equity market opened; resuming quote refresh.");
                        _lastMarketOpenState = true;
                    }
                    await RefreshOnceAsync(stoppingToken);
                }
                else if (_lastMarketOpenState)
                {
                    logger.LogInformation("US equity market is closed; pausing quote refresh.");
                    _lastMarketOpenState = false;
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Quote refresh iteration failed");
            }

            try { await Task.Delay(delay, stoppingToken); } catch { break; }
        }
    }

    private async Task RefreshOnceAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StockyDbContext>();
        var provider = scope.ServiceProvider.GetRequiredService<IMarketDataProvider>();
        var evaluator = scope.ServiceProvider.GetRequiredService<AlertEvaluator>();

        var symbols = await db.Holdings.Select(h => h.Symbol)
            .Union(db.WatchlistItems.Select(w => w.Symbol))
            .Union(db.Transactions
                .Where(t => t.Symbol != null && t.Symbol != "")
                .Select(t => t.Symbol!))
            .Distinct()
            .ToListAsync(ct);
        if (symbols.Count == 0) return;

        var quotes = await provider.GetQuotesAsync(symbols, ct);
        foreach (var q in quotes)
        {
            db.PriceQuotes.Add(new PriceQuote
            {
                Symbol = q.Symbol,
                Price = q.Price,
                Change = q.Change,
                ChangePercent = q.ChangePercent,
                AsOf = q.AsOf
            });
        }
        await db.SaveChangesAsync(ct);
        await evaluator.EvaluateAsync(quotes, ct);

        // M8 #1 — fan-out real-time ticks to SignalR subscribers.
        var broadcaster = scope.ServiceProvider.GetService<PriceTickBroadcaster>();
        if (broadcaster is not null)
        {
            try { await broadcaster.BroadcastAsync(quotes, ct); }
            catch (Exception ex) { logger.LogDebug(ex, "PriceTick broadcast failed"); }
        }

        logger.LogInformation("Refreshed {Count} quotes", quotes.Count);
    }

    /// <summary>
    /// US equity regular trading hours: Mon-Fri, 09:30-16:00 America/New_York.
    /// Federal market holidays are NOT modelled — a full holiday calendar
    /// can be added later. Off-hours requests just no-op cheaply.
    /// </summary>
    internal static bool IsUsEquityMarketOpen(DateTimeOffset utcNow)
    {
        var et = TimeZoneInfo.ConvertTime(utcNow, EasternTz);
        if (et.DayOfWeek == DayOfWeek.Saturday || et.DayOfWeek == DayOfWeek.Sunday) return false;
        var minutes = et.Hour * 60 + et.Minute;
        return minutes >= 9 * 60 + 30 && minutes < 16 * 60;
    }

    private static TimeZoneInfo ResolveEasternTimeZone()
    {
        // Windows uses "Eastern Standard Time"; Linux/macOS use IANA "America/New_York".
        foreach (var id in new[] { "America/New_York", "Eastern Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }
}
