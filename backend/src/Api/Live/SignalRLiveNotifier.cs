using Microsoft.AspNetCore.SignalR;
using Monitoring.Core.Inventory;
using Monitoring.Core.Live;

namespace Monitoring.Api.Live;

public sealed class SignalRLiveNotifier(IHubContext<LiveHub> hub) : ILiveNotifier
{
    public Task StockChanged(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken) =>
        hub.Clients.Group(LiveHub.GroupName).SendAsync("stock.changed", new { productIds }, cancellationToken);

    public Task BatchesExpiring(IReadOnlyCollection<Guid> batchIds, CancellationToken cancellationToken) =>
        hub.Clients.Group(LiveHub.GroupName).SendAsync("batch.expiring", new { batchIds }, cancellationToken);

    public Task BatchesExpired(IReadOnlyCollection<Guid> batchIds, CancellationToken cancellationToken) =>
        hub.Clients.Group(LiveHub.GroupName).SendAsync("batch.expired", new { batchIds }, cancellationToken);

    public Task StopListUpdated(IReadOnlyList<StopListEntry> entries, CancellationToken cancellationToken) =>
        hub.Clients.Group(LiveHub.GroupName).SendAsync("stoplist.updated", entries, cancellationToken);

    public Task HotOffersUpdated(IReadOnlyList<HotOffer> offers, CancellationToken cancellationToken) =>
        hub.Clients.Group(LiveHub.GroupName).SendAsync("hotoffers.updated", offers, cancellationToken);

    public Task OrderDecided(string? connectionId, Guid orderId, string status, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
        {
            return Task.CompletedTask;
        }

        return hub.Clients.Client(connectionId).SendAsync("order.decided", new { orderId, status }, cancellationToken);
    }

    public Task KitchenUpdated(CancellationToken cancellationToken) =>
        hub.Clients.Group(LiveHub.GroupName).SendAsync("kitchen.updated", new { }, cancellationToken);
}
