namespace Stocky.Api.Domain;

public enum TransactionType
{
    Buy,
    Sell,
    Dividend,
    Deposit,
    Withdrawal,
    Split,
    SpinOff,
    Fee,
    // Interest credited to the account (e.g. cash sweep yield). Cash inflow like Dividend.
    Interest,
    // External cash transfer into the account (e.g. ACAT/ACH from another broker).
    // Treated as a cash inflow for ledger purposes; users record outflows as Withdrawal.
    Transfer
}

public class Transaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PortfolioId { get; set; }
    public Portfolio Portfolio { get; set; } = default!;
    public string? Symbol { get; set; }
    public TransactionType Type { get; set; }
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal Fee { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTimeOffset ExecutedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Notes { get; set; }
}
