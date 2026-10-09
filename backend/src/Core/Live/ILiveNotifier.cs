using Monitoring.Core.Inventory;

namespace Monitoring.Core.Live;

public interface ILiveNotifier
{
    Task StockChanged(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken);

    Task BatchesExpiring(IReadOnlyCollection<Guid> batchIds, CancellationToken cancellationToken);

    Task BatchesExpired(IReadOnlyCollection<Guid> batchIds, CancellationToken cancellationToken);

    Task StopListUpdated(IReadOnlyList<StopListEntry> entries, CancellationToken cancellationToken);

    Task HotOffersUpdated(IReadOnlyList<HotOffer> offers, CancellationToken cancellationToken);

    Task OrderDecided(string? connectionId, Guid orderId, string status, CancellationToken cancellationToken);

    Task KitchenUpdated(CancellationToken cancellationToken);
}

public sealed class NoopLiveNotifier : ILiveNotifier
{
    public Task StockChanged(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task BatchesExpiring(IReadOnlyCollection<Guid> batchIds, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task BatchesExpired(IReadOnlyCollection<Guid> batchIds, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopListUpdated(IReadOnlyList<StopListEntry> entries, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task HotOffersUpdated(IReadOnlyList<HotOffer> offers, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task OrderDecided(string? connectionId, Guid orderId, string status, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task KitchenUpdated(CancellationToken cancellationToken) => Task.CompletedTask;
}
