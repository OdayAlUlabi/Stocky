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

    /// <summary>
    /// Bulk import transactions from a CSV body. Expected header:
    /// <c>type,symbol,quantity,price,fee,currency,executedAt,notes</c>
    /// Header is optional; if absent, the first row is treated as data.
    /// Recompute and tax-lot work happen once at the end for speed.
    /// </summary>
    [HttpPost("import")]
    public async Task<ActionResult<ImportTransactionsResult>> Import(Guid portfolioId, ImportTransactionsRequest request, CancellationToken ct)
    {
        var ownerId = User.GetOwnerId();
        var portfolio = await db.Portfolios
            .Include(p => p.Holdings)
            .FirstOrDefaultAsync(p => p.Id == portfolioId && p.OwnerId == ownerId, ct);
        if (portfolio is null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Csv)) return BadRequest("CSV body is empty.");

        var errors = new List<ImportTransactionsRowError>();
        var newSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var imported = 0;

        // Track running quantities per symbol so consecutive Sells in the same
        // batch validate against the cumulative state, not just the DB snapshot.
        var running = portfolio.Holdings.ToDictionary(h => h.Symbol, h => h.Quantity, StringComparer.OrdinalIgnoreCase);

        var lines = request.Csv.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var firstNonEmpty = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? string.Empty;
        var hasHeader = firstNonEmpty.Split(',').Any(c => c.Trim().Equals("type", StringComparison.OrdinalIgnoreCase));
        var startIdx = 0;
        if (hasHeader)
        {
            for (var i = 0; i < lines.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(lines[i])) { startIdx = i + 1; break; }
            }
        }

        for (var i = startIdx; i < lines.Length; i++)
        {
            var rowNumber = i + 1;
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            var cells = SplitCsvRow(line);
            if (cells.Count < 4)
            {
                errors.Add(new ImportTransactionsRowError(rowNumber, "Need at least type,symbol,quantity,price columns."));
                continue;
            }

            var typeStr = cells[0].Trim();
            var symbolRaw = cells.Count > 1 ? cells[1].Trim() : string.Empty;
            var qtyStr = cells.Count > 2 ? cells[2].Trim() : "0";
            var priceStr = cells.Count > 3 ? cells[3].Trim() : "0";
            var feeStr = cells.Count > 4 ? cells[4].Trim() : "0";
            var currencyStr = cells.Count > 5 ? cells[5].Trim() : string.Empty;
            var executedStr = cells.Count > 6 ? cells[6].Trim() : string.Empty;
            var notes = cells.Count > 7 ? string.Join(',', cells.Skip(7)).Trim() : null;

            if (!Enum.TryParse<TransactionType>(typeStr, ignoreCase: true, out var type))
            {
                errors.Add(new ImportTransactionsRowError(rowNumber, $"Unknown transaction type '{typeStr}'."));
                continue;
            }
            var symbol = string.IsNullOrWhiteSpace(symbolRaw) ? null : symbolRaw.ToUpperInvariant();
            if (!decimal.TryParse(qtyStr, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var qty))
            {
                errors.Add(new ImportTransactionsRowError(rowNumber, $"Invalid quantity '{qtyStr}'."));
                continue;
            }
            if (!decimal.TryParse(priceStr, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var price))
            {
                errors.Add(new ImportTransactionsRowError(rowNumber, $"Invalid price '{priceStr}'."));
                continue;
            }
            decimal fee = 0;
            if (!string.IsNullOrWhiteSpace(feeStr) &&
                !decimal.TryParse(feeStr, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out fee))
            {
                errors.Add(new ImportTransactionsRowError(rowNumber, $"Invalid fee '{feeStr}'."));
                continue;
            }
            var currency = string.IsNullOrWhiteSpace(currencyStr) ? portfolio.BaseCurrency : currencyStr.ToUpperInvariant();
            DateTimeOffset executedAt;
            if (string.IsNullOrWhiteSpace(executedStr))
            {
                executedAt = DateTimeOffset.UtcNow;
            }
            else if (!DateTimeOffset.TryParse(executedStr, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out executedAt))
            {
                errors.Add(new ImportTransactionsRowError(rowNumber, $"Invalid executedAt '{executedStr}'."));
                continue;
            }

            if (Validate(type, symbol, qty, price, fee, executedAt) is { } err)
            {
                errors.Add(new ImportTransactionsRowError(rowNumber, err));
                continue;
            }

            if (type == TransactionType.Sell && symbol is not null)
            {
                var available = running.TryGetValue(symbol, out var q) ? q : 0m;
                if (qty > available)
                {
                    errors.Add(new ImportTransactionsRowError(rowNumber, $"Cannot sell {qty} {symbol}; only {available} available at this point in the batch."));
                    continue;
                }
                running[symbol] = available - qty;
            }
            else if (type == TransactionType.Buy && symbol is not null)
            {
                running[symbol] = (running.TryGetValue(symbol, out var q) ? q : 0m) + qty;
            }

            db.Transactions.Add(new Transaction
            {
                PortfolioId = portfolioId,
                Symbol = symbol,
                Type = type,
                Quantity = qty,
                Price = price,
                Fee = fee,
                Currency = currency,
                ExecutedAt = executedAt,
                Notes = notes
            });

            if (symbol is not null && (type == TransactionType.Buy || type == TransactionType.Sell))
            {
                newSymbols.Add(symbol);
            }
            imported++;
        }

        if (imported == 0)
        {
            return Ok(new ImportTransactionsResult(0, errors.Count, errors));
        }

        foreach (var sym in newSymbols)
        {
            await EnsureInstrumentAsync(sym, portfolio.BaseCurrency);
        }

        await db.SaveChangesAsync(ct);
        await RecomputeHoldingsAsync(portfolioId);
        await db.SaveChangesAsync(ct);
        await taxLots.RecomputeAsync(portfolioId);

        return Ok(new ImportTransactionsResult(imported, errors.Count, errors));
    }

    private static List<string> SplitCsvRow(string line)
    {
        // Minimal CSV split with double-quote support; sufficient for our import
        // format where only the notes field may contain commas or quoted text.
        var cells = new List<string>();
        var sb = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else { inQuotes = false; }
                }
                else { sb.Append(ch); }
            }
            else if (ch == '"') { inQuotes = true; }
            else if (ch == ',') { cells.Add(sb.ToString()); sb.Clear(); }
            else { sb.Append(ch); }
        }
        cells.Add(sb.ToString());
        return cells;
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
