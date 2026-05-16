using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;

namespace Stocky.Api.Services;

/// <summary>
/// Reconstructs a day-by-day equity curve for a portfolio from its transaction
/// ledger, marking each held symbol with split-adjusted close prices fetched
/// once from <see cref="IMarketDataProvider.GetDailyBarsAsync"/>. Series spans
/// from the first transaction's date to today, weekdays only.
/// </summary>
public sealed class PortfolioHistoryService(StockyDbContext db, IMarketDataProvider market)
{
    public async Task<PortfolioHistoryDto?> BuildAsync(Guid portfolioId, string ownerId, CancellationToken ct = default)
    {
        var portfolio = await db.Portfolios
            .FirstOrDefaultAsync(p => p.Id == portfolioId && p.OwnerId == ownerId, ct);
        if (portfolio is null) return null;

        var txs = await db.Transactions
            .Where(t => t.PortfolioId == portfolioId)
            .OrderBy(t => t.ExecutedAt)
            .ThenBy(t => t.Id)
            .ToListAsync(ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (txs.Count == 0)
        {
            return new PortfolioHistoryDto(portfolioId, portfolio.BaseCurrency, today, today,
                0m, 0m, 0m, 0m,
                Array.Empty<PortfolioHistoryPointDto>(),
                Array.Empty<PortfolioHistoryEventDto>());
        }

        var startDate = DateOnly.FromDateTime(txs[0].ExecutedAt.UtcDateTime);
        var endDate = today;

        // Fetch daily bars for every symbol that ever traded. One batched call.
        var symbols = txs
            .Where(t => !string.IsNullOrEmpty(t.Symbol))
            .Select(t => t.Symbol!.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var bars = symbols.Count == 0
            ? new Dictionary<string, IReadOnlyList<DailyBarDto>>(StringComparer.OrdinalIgnoreCase)
            : (await market.GetDailyBarsAsync(symbols, startDate, endDate, ct))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        // Index bars by (symbol, date) for O(1) lookup and remember last known.
        var priceByDay = new Dictionary<string, Dictionary<DateOnly, decimal>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (sym, list) in bars)
        {
            var map = new Dictionary<DateOnly, decimal>(list.Count);
            foreach (var b in list) map[b.Date] = b.Close;
            priceByDay[sym] = map;
        }

        // Walk forward day by day. Skip Sat/Sun to keep the series tidy and to
        // match how market quotes behave.
        var holdings = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var lastPrice = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        decimal cash = 0m;
        decimal contributions = 0m;
        var events = new List<PortfolioHistoryEventDto>(txs.Count);
        var series = new List<PortfolioHistoryPointDto>();
        var txIdx = 0;

        for (var day = startDate; day <= endDate; day = day.AddDays(1))
        {
            // Apply all transactions whose date <= day (advance pointer).
            while (txIdx < txs.Count
                   && DateOnly.FromDateTime(txs[txIdx].ExecutedAt.UtcDateTime) <= day)
            {
                var tx = txs[txIdx++];
                ApplyTransaction(tx, holdings, ref cash, ref contributions, lastPrice, events);
            }

            if (day.DayOfWeek == DayOfWeek.Saturday || day.DayOfWeek == DayOfWeek.Sunday)
                continue;

            // Mark every still-held symbol. Carry forward the most recent known
            // price (bar close, else last seen transaction price).
            decimal marketValue = 0m;
            foreach (var (sym, qty) in holdings)
            {
                if (qty == 0m) continue;
                if (priceByDay.TryGetValue(sym, out var map) && map.TryGetValue(day, out var close))
                {
                    lastPrice[sym] = close;
                }
                if (lastPrice.TryGetValue(sym, out var px))
                {
                    marketValue += qty * px;
                }
            }

            series.Add(new PortfolioHistoryPointDto(
                day,
                Math.Round(cash, 2),
                Math.Round(marketValue, 2),
                Math.Round(cash + marketValue, 2),
                Math.Round(contributions, 2)));
        }

        var totalEquity = series.Count == 0 ? 0m : series[^1].TotalEquity;
        var totalReturn = totalEquity - contributions;
        var totalReturnPct = contributions <= 0 ? 0m : Math.Round(totalReturn / contributions * 100m, 2);

        return new PortfolioHistoryDto(
            portfolioId,
            portfolio.BaseCurrency,
            startDate,
            endDate,
            Math.Round(contributions, 2),
            Math.Round(totalEquity, 2),
            Math.Round(totalReturn, 2),
            totalReturnPct,
            series,
            events);
    }

    private static void ApplyTransaction(
        Transaction tx,
        Dictionary<string, decimal> holdings,
        ref decimal cash,
        ref decimal contributions,
        Dictionary<string, decimal> lastPrice,
        List<PortfolioHistoryEventDto> events)
    {
        var date = DateOnly.FromDateTime(tx.ExecutedAt.UtcDateTime);
        var sym = tx.Symbol?.ToUpperInvariant();
        var gross = tx.Quantity * tx.Price;
        decimal amount = 0m;

        switch (tx.Type)
        {
            case TransactionType.Deposit:
                cash += gross;
                contributions += gross;
                amount = gross;
                break;
            case TransactionType.Withdrawal:
                cash -= gross;
                contributions -= gross;
                amount = -gross;
                break;
            case TransactionType.Buy when sym is not null:
                cash -= gross + tx.Fee;
                holdings[sym] = holdings.GetValueOrDefault(sym) + tx.Quantity;
                if (tx.Price > 0) lastPrice[sym] = tx.Price;
                amount = -(gross + tx.Fee);
                break;
            case TransactionType.Sell when sym is not null:
                cash += gross - tx.Fee;
                holdings[sym] = holdings.GetValueOrDefault(sym) - tx.Quantity;
                if (tx.Price > 0) lastPrice[sym] = tx.Price;
                amount = gross - tx.Fee;
                break;
            case TransactionType.Dividend:
                cash += gross;
                amount = gross;
                break;
            case TransactionType.Fee:
                cash -= gross;
                amount = -gross;
                break;
            case TransactionType.Split when sym is not null:
                // Convention: Price > 1 = reverse split ratio (qty /= ratio);
                // 0 < Price < 1 = forward split multiplier (qty *= 1/Price);
                // Price == 0 = unknown ratio (no-op placeholder).
                if (tx.Price > 0 && holdings.TryGetValue(sym, out var heldSplit))
                {
                    var newQty = tx.Price >= 1m ? heldSplit / tx.Price : heldSplit * (1m / tx.Price);
                    holdings[sym] = newQty;
                    if (lastPrice.TryGetValue(sym, out var px) && tx.Price > 0)
                        lastPrice[sym] = px * tx.Price; // inverse adjustment so mark stays sensible
                }
                amount = 0m;
                break;
            case TransactionType.SpinOff:
                // Spin-off placeholders in our CSV don't actually create shares
                // (warrant qty unknown). Logged for the timeline but no cash or
                // holding impact.
                amount = 0m;
                break;
        }

        events.Add(new PortfolioHistoryEventDto(date, tx.Type.ToString(), sym, tx.Quantity, Math.Round(amount, 2), tx.Notes));
    }
}
