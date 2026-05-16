using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;

namespace Stocky.Api.Services;

/// <summary>
/// Daily snapshot writer. Computes mark-to-market value per portfolio using
/// the latest cached quote per symbol and upserts one PortfolioSnapshot row
/// per portfolio per day. SCR-009 (Performance) reads from this table.
/// </summary>
public sealed class SnapshotJob(IServiceProvider services, ILogger<SnapshotJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run once on startup, then every 6 hours. Upsert guarantees the row
        // for "today" is current even if the server restarts.
        var interval = TimeSpan.FromHours(6);
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); } catch { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Snapshot job iteration failed");
            }
            try { await Task.Delay(interval, stoppingToken); } catch { break; }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StockyDbContext>();
        var portfolios = await db.Portfolios.Include(p => p.Holdings).ToListAsync(ct);
        if (portfolios.Count == 0) return;

        var symbols = portfolios.SelectMany(p => p.Holdings.Select(h => h.Symbol)).Distinct().ToList();
        var latest = await db.PriceQuotes
            .Where(q => symbols.Contains(q.Symbol))
            .GroupBy(q => q.Symbol)
            .Select(g => g.OrderByDescending(x => x.AsOf).First())
            .ToDictionaryAsync(q => q.Symbol, q => q, ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var existing = await db.PortfolioSnapshots
            .Where(s => s.Date == today)
            .ToDictionaryAsync(s => s.PortfolioId, s => s, ct);

        foreach (var p in portfolios)
        {
            decimal mv = 0m, cb = 0m, dayPnl = 0m;
            foreach (var h in p.Holdings)
            {
                cb += h.Quantity * h.AverageCost;
                if (latest.TryGetValue(h.Symbol, out var q))
                {
                    mv += h.Quantity * q.Price;
                    if (q.Change.HasValue) dayPnl += h.Quantity * q.Change.Value;
                }
            }
            if (existing.TryGetValue(p.Id, out var snap))
            {
                snap.MarketValue = mv;
                snap.CostBasis = cb;
                snap.DayPnL = dayPnl;
            }
            else
            {
                db.PortfolioSnapshots.Add(new PortfolioSnapshot
                {
                    PortfolioId = p.Id,
                    Date = today,
                    MarketValue = mv,
                    CostBasis = cb,
                    DayPnL = dayPnl
                });
            }
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Wrote snapshots for {Count} portfolios", portfolios.Count);
    }
}
