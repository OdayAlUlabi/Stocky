using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;

namespace Stocky.Api.Services;

/// <summary>
/// Walks active alerts and trips any whose threshold has been crossed by the
/// latest quote. Idempotent: a triggered alert is moved to Triggered and
/// won't re-fire until the user re-arms it.
/// </summary>
public sealed class AlertEvaluator(StockyDbContext db, ILogger<AlertEvaluator> logger)
{
    public async Task EvaluateAsync(IReadOnlyCollection<QuoteDto> quotes, CancellationToken ct = default)
    {
        if (quotes.Count == 0) return;
        var quoteBySymbol = quotes.GroupBy(q => q.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var symbolList = quoteBySymbol.Keys.ToList();
        var alerts = await db.Alerts
            .Where(a => a.Status == AlertStatus.Active && symbolList.Contains(a.Symbol))
            .ToListAsync(ct);
        if (alerts.Count == 0) return;

        var now = DateTimeOffset.UtcNow;
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
            alert.Status = AlertStatus.Triggered;
            alert.TriggeredAt = now;
            alert.TriggeredValue = value;
            triggered++;
        }
        if (triggered > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Triggered {Count} alerts", triggered);
        }
    }
}
