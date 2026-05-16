using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;

namespace Stocky.Api.Services;

/// <summary>
/// M14 #112 — append-only audit log writer. Fire-and-forget from any
/// controller via <see cref="WriteAsync"/>. Failures must never break
/// the underlying mutation, so all exceptions are swallowed.
/// </summary>
public class AuditLogger(StockyDbContext db, IHttpContextAccessor accessor, ILogger<AuditLogger> log)
{
    public async Task WriteAsync(string ownerId, string action, string resource, string? resourceId = null, object? details = null, int? statusCode = null, CancellationToken ct = default)
    {
        try
        {
            var http = accessor.HttpContext;
            var entry = new AuditEntry
            {
                OwnerId = ownerId,
                Action = action,
                Resource = resource,
                ResourceId = resourceId,
                Method = http?.Request.Method,
                Path = http?.Request.Path.Value,
                StatusCode = statusCode,
                ClientIp = http?.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Truncate(http?.Request.Headers.UserAgent.ToString(), 300),
                Details = details is null ? null : Truncate(JsonSerializer.Serialize(details), 2000)
            };
            db.AuditEntries.Add(entry);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Audit write failed for {Resource} {Action}", resource, action);
        }
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= max ? value : value[..max];
    }
}

/// <summary>
/// M14 #114 — cash management. Cash deposits, withdrawals, fees, dividends,
/// and interest are persisted as <see cref="Transaction"/> rows with no
/// associated symbol, then summed per portfolio + currency.
/// </summary>
public class CashService(StockyDbContext db)
{
    private static readonly HashSet<TransactionType> CashTypes = new()
    {
        TransactionType.Deposit,
        TransactionType.Withdrawal,
        TransactionType.Fee,
        TransactionType.Dividend
    };

    public static TransactionType ParseType(string raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "deposit" => TransactionType.Deposit,
        "withdrawal" or "withdraw" => TransactionType.Withdrawal,
        "fee" => TransactionType.Fee,
        "dividend" => TransactionType.Dividend,
        "interest" => TransactionType.Dividend, // interest stored as Dividend kind with note "Interest"
        _ => throw new ArgumentException($"Unsupported cash transaction type '{raw}'")
    };

    /// <summary>Signed amount: deposits/dividends positive, withdrawals/fees negative.</summary>
    public static decimal SignedAmount(TransactionType type, decimal amount)
    {
        var magnitude = Math.Abs(amount);
        return type switch
        {
            TransactionType.Deposit or TransactionType.Dividend => magnitude,
            TransactionType.Withdrawal or TransactionType.Fee => -magnitude,
            _ => amount
        };
    }

    public async Task<List<Transaction>> ListAsync(Guid portfolioId, string ownerId, CancellationToken ct = default)
    {
        return await db.Transactions
            .Where(t => t.PortfolioId == portfolioId
                && t.Portfolio.OwnerId == ownerId
                && CashTypes.Contains(t.Type)
                && t.Symbol == null)
            .OrderByDescending(t => t.ExecutedAt)
            .ToListAsync(ct);
    }

    public async Task<List<(string Currency, decimal Balance, int Count)>> BalancesAsync(Guid portfolioId, string ownerId, CancellationToken ct = default)
    {
        var rows = await db.Transactions
            .Where(t => t.PortfolioId == portfolioId
                && t.Portfolio.OwnerId == ownerId
                && CashTypes.Contains(t.Type)
                && t.Symbol == null)
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.Currency)
            .Select(g => (
                g.Key,
                g.Sum(r => SignedAmount(r.Type, r.Price)),
                g.Count()))
            .OrderBy(g => g.Key)
            .ToList();
    }
}
