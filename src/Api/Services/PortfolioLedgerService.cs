using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;

namespace Stocky.Api.Services;

/// <summary>
/// Computes per-portfolio cash balance and derives current holdings from the
/// transaction journal. Centralises the rules that were previously duplicated
/// across TransactionsController and TransactionImportController so that all
/// transaction types (Buy/Sell/Dividend/Deposit/Withdrawal/Fee/Split/SpinOff)
/// are accounted for consistently.
/// </summary>
public sealed class PortfolioLedgerService(StockyDbContext db)
{
    /// <summary>
    /// Cash balance is the running sum of every transaction's effect on cash:
    /// - Deposit / Dividend / refund            : + qty * price
    /// - Withdrawal / Fee                       : - qty * price
    /// - Buy                                    : - (qty * price + fee)
    /// - Sell                                   : + (qty * price - fee)
    /// - SpinOff (cash-in-lieu)                 : + qty * price
    /// - Split residual (fractional share cash) : + qty * price
    /// </summary>
    public async Task<decimal> GetCashBalanceAsync(Guid portfolioId, CancellationToken ct = default)
    {
        var txs = await db.Transactions
            .Where(t => t.PortfolioId == portfolioId)
            .ToListAsync(ct);
        decimal cash = 0m;
        foreach (var t in txs)
        {
            var gross = t.Quantity * t.Price;
            switch (t.Type)
            {
                case TransactionType.Deposit:
                case TransactionType.Dividend:
                    cash += gross;
                    break;
                case TransactionType.Withdrawal:
                case TransactionType.Fee:
                    cash -= gross;
                    break;
                case TransactionType.Buy:
                    cash -= gross + t.Fee;
                    break;
                case TransactionType.Sell:
                    cash += gross - t.Fee;
                    break;
                case TransactionType.SpinOff:
                case TransactionType.Split:
                    // Position-affecting corporate actions; cash impact (if any)
                    // is captured by separate Deposit/Withdrawal rows so the
                    // ledger stays unambiguous.
                    break;
            }
        }
        return Math.Round(cash, 2);
    }

    /// <summary>
    /// Recompute Holdings rows from the full transaction journal.
    /// Buy/Sell drive running average-cost. Split rows whose Price is greater
    /// than zero are treated as a split ratio: quantity is divided by the
    /// ratio and average cost is multiplied by it (a 1-for-10 reverse split
    /// is Price=10; a 2-for-1 forward split is Price=0.5).
    /// </summary>
    public async Task RecomputeHoldingsAsync(Guid portfolioId, CancellationToken ct = default)
    {
        var txs = await db.Transactions
            .Where(t => t.PortfolioId == portfolioId && t.Symbol != null &&
                        (t.Type == TransactionType.Buy ||
                         t.Type == TransactionType.Sell ||
                         t.Type == TransactionType.Split))
            .OrderBy(t => t.ExecutedAt)
            .ToListAsync(ct);

        var grouped = txs.GroupBy(t => t.Symbol!).ToDictionary(g => g.Key, g => g.ToList());
        var existing = await db.Holdings.Where(h => h.PortfolioId == portfolioId).ToListAsync(ct);
        var bySymbol = existing.ToDictionary(h => h.Symbol);

        foreach (var (symbol, list) in grouped)
        {
            decimal qty = 0m, avg = 0m;
            foreach (var t in list)
            {
                if (t.Type == TransactionType.Buy)
                {
                    var nq = qty + t.Quantity;
                    avg = nq == 0 ? 0 : ((qty * avg) + (t.Quantity * t.Price)) / nq;
                    qty = nq;
                }
                else if (t.Type == TransactionType.Sell)
                {
                    qty -= t.Quantity;
                    if (qty <= 0) { qty = 0; avg = 0; }
                }
                else if (t.Type == TransactionType.Split && t.Price > 0 && qty > 0)
                {
                    qty /= t.Price;
                    avg *= t.Price;
                }
            }

            if (bySymbol.TryGetValue(symbol, out var h))
            {
                if (qty <= 0) db.Holdings.Remove(h);
                else { h.Quantity = qty; h.AverageCost = avg; }
                bySymbol.Remove(symbol);
            }
            else if (qty > 0)
            {
                db.Holdings.Add(new Holding { PortfolioId = portfolioId, Symbol = symbol, Quantity = qty, AverageCost = avg });
            }
        }
        foreach (var orphan in bySymbol.Values) db.Holdings.Remove(orphan);
    }
}
