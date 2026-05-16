using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;

namespace Stocky.Api.Services;

/// <summary>
/// Computes drift vs per-symbol target weights and emits the trade values
/// (in the portfolio's base currency) needed to restore each holding to its
/// target percentage of total portfolio value.
///
/// Symbols held without a target are surfaced with a TargetWeightPercent=0 row
/// so the user can see overweight positions to trim. Targets that do not sum
/// to 100% are accepted as-is — the residual is treated as a cash target.
/// </summary>
public sealed class RebalanceService(StockyDbContext db)
{
    public async Task<RebalanceReportDto?> ComputeAsync(Guid portfolioId, string ownerId, CancellationToken ct = default)
    {
        var portfolio = await db.Portfolios
            .Include(p => p.Holdings)
            .FirstOrDefaultAsync(p => p.Id == portfolioId && p.OwnerId == ownerId, ct);
        if (portfolio is null) return null;

        var heldSymbols = portfolio.Holdings.Where(h => h.Quantity > 0).Select(h => h.Symbol).ToList();
        var targets = await db.RebalanceTargets
            .Where(t => t.PortfolioId == portfolioId)
            .ToDictionaryAsync(t => t.Symbol, t => t.TargetWeightPercent, ct);

        var quoteSymbols = heldSymbols.Concat(targets.Keys).Distinct().ToList();
        var latestPrices = await db.PriceQuotes
            .Where(q => quoteSymbols.Contains(q.Symbol))
            .GroupBy(q => q.Symbol)
            .Select(g => g.OrderByDescending(x => x.AsOf).First())
            .ToDictionaryAsync(q => q.Symbol, q => q.Price, ct);

        var values = new Dictionary<string, decimal>();
        decimal total = 0m;
        foreach (var h in portfolio.Holdings.Where(h => h.Quantity > 0))
        {
            if (!latestPrices.TryGetValue(h.Symbol, out var px)) continue;
            var v = h.Quantity * px;
            values[h.Symbol] = v;
            total += v;
        }

        var allSymbols = values.Keys.Concat(targets.Keys).Distinct().OrderBy(s => s);
        var suggestions = new List<RebalanceSuggestionDto>();
        foreach (var sym in allSymbols)
        {
            var currentValue = values.GetValueOrDefault(sym);
            var currentPct = total > 0 ? Math.Round(currentValue / total * 100m, 4) : 0m;
            var targetPct = targets.GetValueOrDefault(sym);
            var drift = Math.Round(currentPct - targetPct, 4);
            var targetValue = Math.Round(total * targetPct / 100m, 2);
            var tradeValue = Math.Round(targetValue - currentValue, 2);
            var action = tradeValue switch
            {
                > 0.01m => "Buy",
                < -0.01m => "Sell",
                _ => "Hold",
            };
            suggestions.Add(new RebalanceSuggestionDto(
                sym, Math.Round(currentValue, 2), currentPct, targetPct, drift, tradeValue, action));
        }

        return new RebalanceReportDto(
            portfolioId,
            portfolio.BaseCurrency,
            Math.Round(total, 2),
            targets.Values.Sum(),
            suggestions);
    }

    public async Task<IReadOnlyList<RebalanceTargetDto>> GetTargetsAsync(Guid portfolioId, string ownerId, CancellationToken ct = default)
    {
        var exists = await db.Portfolios.AnyAsync(p => p.Id == portfolioId && p.OwnerId == ownerId, ct);
        if (!exists) return Array.Empty<RebalanceTargetDto>();
        return await db.RebalanceTargets
            .Where(t => t.PortfolioId == portfolioId)
            .OrderBy(t => t.Symbol)
            .Select(t => new RebalanceTargetDto(t.Symbol, t.TargetWeightPercent))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Replaces the target set for a portfolio. Symbols with weight 0 are removed; symbols
    /// not present are deleted. The total may be less than 100% (residual = cash target).
    /// </summary>
    public async Task<IReadOnlyList<RebalanceTargetDto>?> SetTargetsAsync(
        Guid portfolioId, string ownerId, IReadOnlyList<RebalanceTargetDto> next, CancellationToken ct = default)
    {
        var exists = await db.Portfolios.AnyAsync(p => p.Id == portfolioId && p.OwnerId == ownerId, ct);
        if (!exists) return null;

        // Reject negatives or totals > 100% to avoid nonsensical plans.
        if (next.Any(t => t.TargetWeightPercent < 0)) throw new ArgumentException("Target weights cannot be negative.");
        var sum = next.Sum(t => t.TargetWeightPercent);
        if (sum > 100m + 0.001m) throw new ArgumentException("Target weights cannot sum above 100%.");

        var existing = await db.RebalanceTargets
            .Where(t => t.PortfolioId == portfolioId)
            .ToDictionaryAsync(t => t.Symbol.ToUpperInvariant(), ct);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in next)
        {
            var sym = t.Symbol.ToUpperInvariant();
            seen.Add(sym);
            if (t.TargetWeightPercent == 0m)
            {
                if (existing.TryGetValue(sym, out var dead)) db.RebalanceTargets.Remove(dead);
                continue;
            }
            if (existing.TryGetValue(sym, out var row))
            {
                row.TargetWeightPercent = t.TargetWeightPercent;
                row.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                db.RebalanceTargets.Add(new RebalanceTarget
                {
                    PortfolioId = portfolioId,
                    Symbol = sym,
                    TargetWeightPercent = t.TargetWeightPercent,
                });
            }
        }
        foreach (var (sym, row) in existing)
        {
            if (!seen.Contains(sym)) db.RebalanceTargets.Remove(row);
        }

        await db.SaveChangesAsync(ct);
        return await GetTargetsAsync(portfolioId, ownerId, ct);
    }
}
