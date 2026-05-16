namespace Stocky.Api.Domain;

public class Watchlist
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!;
    public string Name { get; set; } = default!;
    public List<WatchlistItem> Items { get; set; } = new();
}

public class WatchlistItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WatchlistId { get; set; }
    public Watchlist Watchlist { get; set; } = default!;
    public string Symbol { get; set; } = default!;
    public Instrument Instrument { get; set; } = default!;
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}
