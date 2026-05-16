namespace Stocky.Api.Domain;

public class Portfolio
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!; // Entra ID object id (oid claim)
    public string Name { get; set; } = default!;
    public string BaseCurrency { get; set; } = "USD";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<Holding> Holdings { get; set; } = new();
    public List<Transaction> Transactions { get; set; } = new();
}
