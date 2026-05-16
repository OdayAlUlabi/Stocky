using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Stocky.Api.Dtos;

namespace Stocky.Api.Services;

/// <summary>
/// M8 #1 — Real-time quote streaming hub at /hubs/prices. Clients call
/// <c>Subscribe(symbols)</c> to join symbol-named groups; QuoteRefresher
/// broadcasts <c>"price"</c> ticks to subscribers each refresh cycle.
/// </summary>
[Authorize]
public sealed class PricesHub : Hub
{
    public async Task Subscribe(string[] symbols)
    {
        if (symbols is null) return;
        foreach (var s in symbols)
        {
            if (string.IsNullOrWhiteSpace(s)) continue;
            await Groups.AddToGroupAsync(Context.ConnectionId, s.ToUpperInvariant());
        }
    }

    public async Task Unsubscribe(string[] symbols)
    {
        if (symbols is null) return;
        foreach (var s in symbols)
        {
            if (string.IsNullOrWhiteSpace(s)) continue;
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, s.ToUpperInvariant());
        }
    }
}

/// <summary>
/// Helper that pushes price ticks to symbol groups. Injected into
/// QuoteRefresher so each refresh fan-outs through the hub.
/// </summary>
public sealed class PriceTickBroadcaster(IHubContext<PricesHub> hub)
{
    public Task BroadcastAsync(IEnumerable<QuoteDto> quotes, CancellationToken ct = default)
    {
        var tasks = new List<Task>();
        foreach (var q in quotes)
        {
            var tick = new PriceTickDto(q.Symbol, q.Price, q.Change, q.ChangePercent, q.AsOf);
            tasks.Add(hub.Clients.Group(q.Symbol).SendAsync("price", tick, ct));
        }
        return Task.WhenAll(tasks);
    }
}
