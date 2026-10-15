using Microsoft.AspNetCore.SignalR;
using Monitoring.Core.Inventory;
using Monitoring.Core.Live;

namespace Monitoring.Api.Live;

/// <summary>Кладёт события в группу live. Имена событий совпадают с клиентом: stock.changed, stoplist.updated, shift.updated.</summary>
public sealed class SignalRLiveNotifier(IHubContext<LiveHub> hub) : ILiveNotifier
{
    public Task StockChanged(IReadOnlyCollection<Guid> productIds, IReadOnlyCollection<string> depletedNames, CancellationToken cancellationToken) =>
        hub.Clients.Group(LiveHub.GroupName).SendAsync("stock.changed", new { productIds, depleted = depletedNames }, cancellationToken);

    public Task BatchesExpiring(IReadOnlyList<LiveBatchNotice> batches, CancellationToken cancellationToken) =>
        hub.Clients.Group(LiveHub.GroupName).SendAsync("batch.expiring", new { batches }, cancellationToken);

    public Task BatchesExpired(IReadOnlyList<LiveBatchNotice> batches, CancellationToken cancellationToken) =>
        hub.Clients.Group(LiveHub.GroupName).SendAsync("batch.expired", new { batches }, cancellationToken);

    public Task StopListUpdated(IReadOnlyList<ShiftTile> entries, CancellationToken cancellationToken) =>
        hub.Clients.Group(LiveHub.GroupName).SendAsync("stoplist.updated", entries, cancellationToken);

    public Task ShiftUpdated(CancellationToken cancellationToken) =>
        hub.Clients.Group(LiveHub.GroupName).SendAsync("shift.updated", new { }, cancellationToken);

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

    public Task TableProblem(IReadOnlyCollection<Guid> waiterIds, string tableNumber, string dishes, string note, CancellationToken cancellationToken)
    {
        if (waiterIds.Count == 0)
        {
            return Task.CompletedTask;
        }

        return hub.Clients.Users(waiterIds.Select(id => id.ToString()).ToList())
            .SendAsync("table.problem", new { tableNumber, dishes, note }, cancellationToken);
    }

    public Task ChatPosted(Guid id, string author, string role, string text, DateTime at, string channel, Guid? authorId, Guid? recipientId, CancellationToken cancellationToken)
    {
        var payload = new { id, author, role, text, at, channel, authorId, recipientId };
        if (recipientId is null || authorId is null)
        {
            return hub.Clients.Group(LiveHub.GroupName).SendAsync("chat.message", payload, cancellationToken);
        }

        return hub.Clients.Users(authorId.Value.ToString(), recipientId.Value.ToString()).SendAsync("chat.message", payload, cancellationToken);
    }

    public Task ChatAddressed(IReadOnlyCollection<Guid> userIds, Guid id, string author, string role, string text, DateTime at, string channel, Guid? authorId, CancellationToken cancellationToken)
    {
        var payload = new { id, author, role, text, at, channel, authorId, recipientId = (Guid?)null };
        return hub.Clients.Users(userIds.Select(userId => userId.ToString()).ToList()).SendAsync("chat.message", payload, cancellationToken);
    }
}
