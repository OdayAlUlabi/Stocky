using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;

namespace Stocky.Api.Services;

/// <summary>
/// Derives current per-portfolio holdings (Quantity + AverageCost) by replaying
/// the transaction journal in-memory — never reads from <c>db.Holdings</c>.
/// Mirrors the rules in <see cref="PortfolioLedgerService.RecomputeHoldingsAsync"/>:
/// Buy/Sell drive running average cost; Split rows with Price &gt; 0 are a
/// ratio (qty /= ratio, avg *= ratio); SpinOff rows add shares at zero
/// incremental cost (existing basis preserved).
/// The returned <see cref="Holding"/> instances are NOT attached to the
/// <see cref="StockyDbContext"/> and carry <c>Id = Guid.Empty</c>.
/// </summary>
public sealed class HoldingsCalculator(StockyDbContext db)
{
    public async Task<IReadOnlyList<Holding>> ComputeAsync(Guid portfolioId, CancellationToken ct = default)
    {
        var txs = await db.Transactions
            .Where(t => t.PortfolioId == portfolioId && t.Symbol != null &&
                        (t.Type == TransactionType.Buy ||
                         t.Type == TransactionType.Sell ||
                         t.Type == TransactionType.Split ||
                         t.Type == TransactionType.SpinOff))
            .OrderBy(t => t.ExecutedAt)
            .ToListAsync(ct);

        return Project(portfolioId, txs);
    }

    /// <summary>
    /// Bulk variant: replays the journal for every portfolio in a single
    /// query, grouped by (portfolio, symbol). Used by the dashboard so it
    /// can summarise multiple portfolios without per-portfolio round trips.
    /// </summary>
    public async Task<IReadOnlyList<Holding>> ComputeManyAsync(
        IReadOnlyCollection<Guid> portfolioIds, CancellationToken ct = default)
    {
        if (portfolioIds.Count == 0) return Array.Empty<Holding>();

        var txs = await db.Transactions
            .Where(t => portfolioIds.Contains(t.PortfolioId) && t.Symbol != null &&
                        (t.Type == TransactionType.Buy ||
                         t.Type == TransactionType.Sell ||
                         t.Type == TransactionType.Split ||
                         t.Type == TransactionType.SpinOff))
            .OrderBy(t => t.ExecutedAt)
            .ToListAsync(ct);

        var result = new List<Holding>(txs.Count);
        foreach (var byPortfolio in txs.GroupBy(t => t.PortfolioId))
        {
            result.AddRange(Project(byPortfolio.Key, byPortfolio.ToList()));
        }
        return result;
    }

    private static List<Holding> Project(Guid portfolioId, List<Transaction> txs)
    {
        var grouped = txs.GroupBy(t => t.Symbol!).ToDictionary(g => g.Key, g => g.ToList());
        var result = new List<Holding>(grouped.Count);
        foreach (var (symbol, list) in grouped)
        {
            decimal qty = 0m, avg = 0m;
            foreach (var t in list)
            {
                switch (t.Type)
                {
                    case TransactionType.Buy:
                        {
                            var nq = qty + t.Quantity;
                            avg = nq == 0 ? 0 : ((qty * avg) + (t.Quantity * t.Price)) / nq;
                            qty = nq;
                            break;
                        }
                    case TransactionType.Sell:
                        qty -= t.Quantity;
                        if (qty <= 0) { qty = 0; avg = 0; }
                        break;
                    case TransactionType.Split when t.Price > 0 && qty > 0:
                        qty /= t.Price;
                        avg *= t.Price;
                        break;
                    case TransactionType.SpinOff when t.Quantity > 0:
                        {
                            var totalCost = qty * avg; // existing basis preserved
                            qty += t.Quantity;
                            avg = qty == 0 ? 0 : totalCost / qty;
                            break;
                        }
                }
            }
            if (qty > 0)
            {
                result.Add(new Holding
                {
                    Id = Guid.Empty,
                    PortfolioId = portfolioId,
                    Symbol = symbol,
                    Quantity = qty,
                    AverageCost = avg
                });
            }
        }
        return result;
    }
}
