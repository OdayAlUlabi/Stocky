using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;

namespace Stocky.Api.Services;

/// <summary>
/// IRS §1091 wash-sale detector. A realised loss is "disallowed" to the extent that
/// substantially identical shares are reacquired within ±30 days of the sale.
///
/// Simplifications:
///   - "Substantially identical" == same ticker symbol (Stocky doesn't model
///     option chains or sibling ETFs).
///   - Replacement shares are allocated greedily (FIFO by loss date) so a single
///     buy is never counted against more than one loss.
///   - Disallowed amount per lot = |loss| * min(replacementShares, lotShares) / lotShares.
///   - This is a reporting helper only; we do NOT mutate cost basis here.
/// </summary>
public sealed class WashSaleService(StockyDbContext db)
{
    public async Task<WashSaleReportDto> ComputeAsync(Guid portfolioId, int year, CancellationToken ct = default)
    {
        var start = new DateTimeOffset(new DateTime(year, 1, 1), TimeSpan.Zero);
        var end = start.AddYears(1);

        // Losses in the target year, ordered FIFO so earlier sells claim replacement shares first.
        var losses = await db.RealizedGains
            .Where(g => g.PortfolioId == portfolioId && g.SoldAt >= start && g.SoldAt < end && g.Gain < 0)
            .OrderBy(g => g.SoldAt)
            .ToListAsync(ct);

        if (losses.Count == 0)
        {
            return new WashSaleReportDto(year, 0m, 0m, Array.Empty<WashSaleAdjustmentDto>());
        }

        // Pull buys for the symbols involved, padded by 30 days on each side.
        var symbols = losses.Select(l => l.Symbol).Distinct().ToList();
        var earliest = losses.Min(l => l.SoldAt).AddDays(-30);
        var latest = losses.Max(l => l.SoldAt).AddDays(30);

        var buys = await db.Transactions
            .Where(t => t.PortfolioId == portfolioId
                        && t.Type == TransactionType.Buy
                        && t.Symbol != null
                        && symbols.Contains(t.Symbol)
                        && t.ExecutedAt >= earliest
                        && t.ExecutedAt <= latest)
            .OrderBy(t => t.ExecutedAt)
            .Select(t => new { t.Id, t.Symbol, t.ExecutedAt, t.Quantity })
            .ToListAsync(ct);

        // Track remaining replacement shares per buy transaction so we don't double-count.
        var remaining = buys.ToDictionary(b => b.Id, b => b.Quantity);

        var adjustments = new List<WashSaleAdjustmentDto>();
        decimal totalDisallowed = 0m;
        decimal totalLoss = 0m;

        foreach (var loss in losses)
        {
            totalLoss += Math.Abs(loss.Gain);
            var windowStart = loss.SoldAt.AddDays(-30);
            var windowEnd = loss.SoldAt.AddDays(30);

            var replacements = new List<WashSaleReplacementDto>();
            decimal sharesClaimed = 0m;

            foreach (var b in buys.Where(x => x.Symbol == loss.Symbol && x.ExecutedAt >= windowStart && x.ExecutedAt <= windowEnd))
            {
                if (sharesClaimed >= loss.Quantity) break;
                var avail = remaining[b.Id];
                if (avail <= 0m) continue;

                var need = loss.Quantity - sharesClaimed;
                var take = Math.Min(avail, need);
                remaining[b.Id] = avail - take;
                sharesClaimed += take;
                replacements.Add(new WashSaleReplacementDto(b.Id, b.ExecutedAt, take));
            }

            if (sharesClaimed <= 0m) continue;

            var ratio = sharesClaimed / loss.Quantity;
            var disallowed = Math.Round(Math.Abs(loss.Gain) * ratio, 2, MidpointRounding.AwayFromZero);
            totalDisallowed += disallowed;

            adjustments.Add(new WashSaleAdjustmentDto(
                LotId: loss.Id,
                Symbol: loss.Symbol,
                SoldAt: loss.SoldAt,
                LotQuantity: loss.Quantity,
                LotLoss: loss.Gain,
                ReplacementShares: sharesClaimed,
                DisallowedLoss: disallowed,
                AllowedLoss: Math.Round(loss.Gain + disallowed, 2, MidpointRounding.AwayFromZero), // loss is negative; disallowed is positive
                Replacements: replacements));
        }

        return new WashSaleReportDto(
            Year: year,
            TotalLoss: Math.Round(-totalLoss, 2),
            DisallowedLoss: Math.Round(-totalDisallowed, 2), // negative number for UX consistency with gain sign
            Adjustments: adjustments);
    }
}
