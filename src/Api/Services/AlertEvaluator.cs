using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;

namespace Stocky.Api.Services;

/// <summary>
/// Price-tick evaluator. Walks active Price-type alerts and dispatches any
/// whose threshold is crossed by the latest quote. Idempotent: a triggered
/// alert is moved to Triggered and won't re-fire until the user re-arms it.
/// Snoozed alerts are skipped entirely.
/// </summary>
public sealed class AlertEvaluator(
    StockyDbContext db,
    AlertDispatcher dispatcher,
    ILogger<AlertEvaluator> logger)
{
    public async Task EvaluateAsync(IReadOnlyCollection<QuoteDto> quotes, CancellationToken ct = default)
    {
        if (quotes.Count == 0) return;
        var quoteBySymbol = quotes.GroupBy(q => q.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var symbolList = quoteBySymbol.Keys.ToList();
        var now = DateTimeOffset.UtcNow;
        var alerts = await db.Alerts
            .Where(a => a.Status == AlertStatus.Active
                        && a.Type == AlertType.Price
                        && symbolList.Contains(a.Symbol)
                        && (a.SnoozedUntil == null || a.SnoozedUntil <= now))
            .ToListAsync(ct);
        if (alerts.Count == 0) return;

        var triggered = 0;
        foreach (var alert in alerts)
        {
            if (!quoteBySymbol.TryGetValue(alert.Symbol, out var q)) continue;
            var value = alert.Condition switch
            {
                AlertCondition.PriceAbove or AlertCondition.PriceBelow => q.Price,
                AlertCondition.DayChangePercentAbove or AlertCondition.DayChangePercentBelow => q.ChangePercent ?? 0m,
                _ => 0m
            };
            var fire = alert.Condition switch
            {
                AlertCondition.PriceAbove => value >= alert.Threshold,
                AlertCondition.PriceBelow => value <= alert.Threshold,
                AlertCondition.DayChangePercentAbove => value >= alert.Threshold,
                AlertCondition.DayChangePercentBelow => value <= alert.Threshold,
                _ => false
            };
            if (!fire) continue;
            var msg = alert.Condition switch
            {
                AlertCondition.PriceAbove => $"{alert.Symbol} crossed ≥ {alert.Threshold} (now {value:0.##})",
                AlertCondition.PriceBelow => $"{alert.Symbol} crossed ≤ {alert.Threshold} (now {value:0.##})",
                AlertCondition.DayChangePercentAbove => $"{alert.Symbol} day change {value:0.##}% ≥ {alert.Threshold}%",
                AlertCondition.DayChangePercentBelow => $"{alert.Symbol} day change {value:0.##}% ≤ {alert.Threshold}%",
                _ => $"{alert.Symbol} alert fired"
            };
            await dispatcher.TripAsync(alert, value, msg, $"quote:{q.AsOf:O}", ct);
            triggered++;
        }
        if (triggered > 0)
            logger.LogInformation("Triggered {Count} price alerts", triggered);
    }
}
