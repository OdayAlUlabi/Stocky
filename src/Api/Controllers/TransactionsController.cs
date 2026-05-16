using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;
using Stocky.Api.Services;

namespace Stocky.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/portfolios/{portfolioId:guid}/transactions")]
public class TransactionsController(StockyDbContext db, TaxLotService taxLots, PortfolioLedgerService ledger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TransactionDto>>> List(Guid portfolioId)
    {
        var ownerId = User.GetOwnerId();
        var portfolio = await db.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId && p.OwnerId == ownerId);
        if (portfolio is null) return NotFound();

        var items = await db.Transactions
            .Where(t => t.PortfolioId == portfolioId)
            .OrderByDescending(t => t.ExecutedAt)
            .Select(t => new TransactionDto(t.Id, t.Symbol, t.Type.ToString(), t.Quantity, t.Price, t.Fee, t.Currency, t.ExecutedAt, t.Notes))
            .ToListAsync();
        return Ok(items);
    }

    [HttpPost]
    public async Task<ActionResult<TransactionDto>> Create(Guid portfolioId, CreateTransactionRequest request)
    {
        var ownerId = User.GetOwnerId();
        var portfolio = await db.Portfolios
            .Include(p => p.Holdings)
            .FirstOrDefaultAsync(p => p.Id == portfolioId && p.OwnerId == ownerId);
        if (portfolio is null) return NotFound();

        if (!Enum.TryParse<TransactionType>(request.Type, ignoreCase: true, out var type))
            return BadRequest($"Invalid transaction type '{request.Type}'.");

        var symbol = string.IsNullOrWhiteSpace(request.Symbol) ? null : request.Symbol.Trim().ToUpperInvariant();
        var currency = string.IsNullOrWhiteSpace(request.Currency) ? portfolio.BaseCurrency : request.Currency.ToUpperInvariant();
        var executedAt = request.ExecutedAt == default ? DateTimeOffset.UtcNow : request.ExecutedAt;

        if (Validate(type, symbol, request.Quantity, request.Price, request.Fee, executedAt) is { } err)
            return BadRequest(err);

        if (type == TransactionType.Sell && symbol is not null)
        {
            var holding = portfolio.Holdings.FirstOrDefault(h => h.Symbol == symbol);
            var available = holding?.Quantity ?? 0m;
            if (request.Quantity > available)
                return BadRequest($"Cannot sell {request.Quantity} {symbol}; only {available} available.");
        }

        var tx = new Transaction
        {
            PortfolioId = portfolioId,
            Symbol = symbol,
            Type = type,
            Quantity = request.Quantity,
            Price = request.Price,
            Fee = request.Fee,
            Currency = currency,
            ExecutedAt = executedAt,
            Notes = request.Notes
        };
        db.Transactions.Add(tx);

        if (symbol is not null && (type == TransactionType.Buy || type == TransactionType.Sell))
        {
            await EnsureInstrumentAsync(symbol, currency);
        }

        await db.SaveChangesAsync();
        await RecomputeHoldingsAsync(portfolioId);
        await db.SaveChangesAsync();
        await taxLots.RecomputeAsync(portfolioId);

        return CreatedAtAction(nameof(List), new { portfolioId },
            new TransactionDto(tx.Id, tx.Symbol, tx.Type.ToString(), tx.Quantity, tx.Price, tx.Fee, tx.Currency, tx.ExecutedAt, tx.Notes));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TransactionDto>> Update(Guid portfolioId, Guid id, CreateTransactionRequest request)
    {
        var ownerId = User.GetOwnerId();
        var portfolio = await db.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId && p.OwnerId == ownerId);
        if (portfolio is null) return NotFound();

        var tx = await db.Transactions.FirstOrDefaultAsync(t => t.Id == id && t.PortfolioId == portfolioId);
        if (tx is null) return NotFound();

        if (!Enum.TryParse<TransactionType>(request.Type, ignoreCase: true, out var type))
            return BadRequest($"Invalid transaction type '{request.Type}'.");

        var symbol = string.IsNullOrWhiteSpace(request.Symbol) ? null : request.Symbol.Trim().ToUpperInvariant();
        var currency = string.IsNullOrWhiteSpace(request.Currency) ? portfolio.BaseCurrency : request.Currency.ToUpperInvariant();
        var executedAt = request.ExecutedAt == default ? tx.ExecutedAt : request.ExecutedAt;

        if (Validate(type, symbol, request.Quantity, request.Price, request.Fee, executedAt) is { } err)
            return BadRequest(err);

        tx.Symbol = symbol;
        tx.Type = type;
        tx.Quantity = request.Quantity;
        tx.Price = request.Price;
        tx.Fee = request.Fee;
        tx.Currency = currency;
        tx.ExecutedAt = executedAt;
        tx.Notes = request.Notes;

        if (symbol is not null && (type == TransactionType.Buy || type == TransactionType.Sell))
        {
            await EnsureInstrumentAsync(symbol, currency);
        }

        await db.SaveChangesAsync();
        await RecomputeHoldingsAsync(portfolioId);
        await db.SaveChangesAsync();
        await taxLots.RecomputeAsync(portfolioId);

        return new TransactionDto(tx.Id, tx.Symbol, tx.Type.ToString(), tx.Quantity, tx.Price, tx.Fee, tx.Currency, tx.ExecutedAt, tx.Notes);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid portfolioId, Guid id)
    {
        var ownerId = User.GetOwnerId();
        var portfolio = await db.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId && p.OwnerId == ownerId);
        if (portfolio is null) return NotFound();

        var tx = await db.Transactions.FirstOrDefaultAsync(t => t.Id == id && t.PortfolioId == portfolioId);
        if (tx is null) return NotFound();

        db.Transactions.Remove(tx);
        await db.SaveChangesAsync();
        await RecomputeHoldingsAsync(portfolioId);
        await db.SaveChangesAsync();
        await taxLots.RecomputeAsync(portfolioId);
        return NoContent();
    }

    private static string? Validate(TransactionType type, string? symbol, decimal qty, decimal price, decimal fee, DateTimeOffset executedAt)
    {
        if (qty <= 0) return "Quantity must be greater than zero.";
        if (price < 0) return "Price cannot be negative.";
        if (fee < 0) return "Fee cannot be negative.";
        if (executedAt > DateTimeOffset.UtcNow.AddMinutes(5)) return "Executed date cannot be in the future.";
        if ((type == TransactionType.Buy || type == TransactionType.Sell) && string.IsNullOrWhiteSpace(symbol))
            return "Symbol is required for buy/sell trades.";
        return null;
    }

    private async Task EnsureInstrumentAsync(string symbol, string currency)
    {
        if (!await db.Instruments.AnyAsync(i => i.Symbol == symbol))
        {
            db.Instruments.Add(new Instrument { Symbol = symbol, Name = symbol, Exchange = "UNKNOWN", Currency = currency, AssetClass = "Equity" });
        }
    }

    /// <summary>
    /// Replays the full transaction history to derive current holdings.
    /// Delegates to <see cref="PortfolioLedgerService"/> so Buy/Sell/Split
    /// semantics stay in one place.
    /// </summary>
    private Task RecomputeHoldingsAsync(Guid portfolioId) => ledger.RecomputeHoldingsAsync(portfolioId);
}
