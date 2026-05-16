using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;

namespace Stocky.Api.Services;

/// <summary>
/// FIFO tax-lot engine. Recomputes from scratch on every call so it is
/// idempotent under edits/deletes of past transactions.
/// </summary>
public sealed class TaxLotService(StockyDbContext db)
{
    private const int LongTermDays = 365;

    public async Task RecomputeAsync(Guid portfolioId, CancellationToken ct = default)
    {
        var oldLots = await db.TaxLots.Where(l => l.PortfolioId == portfolioId).ToListAsync(ct);
        var oldGains = await db.RealizedGains.Where(g => g.PortfolioId == portfolioId).ToListAsync(ct);
        db.TaxLots.RemoveRange(oldLots);
        db.RealizedGains.RemoveRange(oldGains);

        var txs = await db.Transactions
            .Where(t => t.PortfolioId == portfolioId && t.Symbol != null &&
                        (t.Type == TransactionType.Buy ||
                         t.Type == TransactionType.Sell ||
                         t.Type == TransactionType.Split))
            .OrderBy(t => t.ExecutedAt)
            .ToListAsync(ct);

        var open = new Dictionary<string, List<TaxLot>>(StringComparer.OrdinalIgnoreCase);
        foreach (var tx in txs)
        {
            var symbol = tx.Symbol!;
            if (!open.TryGetValue(symbol, out var lots))
            {
                lots = new List<TaxLot>();
                open[symbol] = lots;
            }

            if (tx.Type == TransactionType.Buy)
            {
                var lot = new TaxLot
                {
                    PortfolioId = portfolioId,
                    Symbol = symbol,
                    OpenedByTransactionId = tx.Id,
                    OpenedAt = tx.ExecutedAt,
                    Quantity = tx.Quantity,
                    RemainingQuantity = tx.Quantity,
                    CostPerShare = tx.Price + (tx.Quantity == 0 ? 0 : tx.Fee / tx.Quantity)
                };
                lots.Add(lot);
            }
            else if (tx.Type == TransactionType.Split)
            {
                if (tx.Price <= 0) continue;
                foreach (var lot in lots)
                {
                    lot.Quantity /= tx.Price;
                    lot.RemainingQuantity /= tx.Price;
                    lot.CostPerShare *= tx.Price;
                }
            }
            else if (tx.Type == TransactionType.Sell)
            {
                var remainingToSell = tx.Quantity;
                var proceedsPerShare = tx.Price - (tx.Quantity == 0 ? 0 : tx.Fee / tx.Quantity);
                foreach (var lot in lots.Where(l => l.RemainingQuantity > 0).OrderBy(l => l.OpenedAt).ToList())
                {
                    if (remainingToSell <= 0) break;
                    var take = Math.Min(lot.RemainingQuantity, remainingToSell);
                    var costBasis = take * lot.CostPerShare;
                    var proceeds = take * proceedsPerShare;
                    var holdDays = (tx.ExecutedAt - lot.OpenedAt).TotalDays;
                    db.RealizedGains.Add(new RealizedGain
                    {
                        PortfolioId = portfolioId,
                        Symbol = symbol,
                        SellTransactionId = tx.Id,
                        LotId = lot.Id,
                        AcquiredAt = lot.OpenedAt,
                        SoldAt = tx.ExecutedAt,
                        Quantity = take,
                        CostBasis = costBasis,
                        Proceeds = proceeds,
                        Gain = proceeds - costBasis,
                        IsLongTerm = holdDays >= LongTermDays
                    });
                    lot.RemainingQuantity -= take;
                    remainingToSell -= take;
                }
            }
        }

        foreach (var lot in open.Values.SelectMany(v => v))
        {
            db.TaxLots.Add(lot);
        }
        await db.SaveChangesAsync(ct);
    }
}
