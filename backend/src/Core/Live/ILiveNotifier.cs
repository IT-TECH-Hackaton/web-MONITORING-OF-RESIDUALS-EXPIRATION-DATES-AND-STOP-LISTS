using Monitoring.Core.Inventory;

namespace Monitoring.Core.Live;

/// <summary>
/// События в группу SignalR live. Клиенты по ним перечитывают экраны.
/// stoplist.updated несёт плитки смены, shift.updated — пустой сигнал «смена изменилась»,
/// order.decided уходит только соединению, которое создало заказ.
/// </summary>
public interface ILiveNotifier
{
    Task StockChanged(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken);

    Task BatchesExpiring(IReadOnlyCollection<Guid> batchIds, CancellationToken cancellationToken);

    Task BatchesExpired(IReadOnlyCollection<Guid> batchIds, CancellationToken cancellationToken);

    Task StopListUpdated(IReadOnlyList<ShiftTile> entries, CancellationToken cancellationToken);

    Task ShiftUpdated(CancellationToken cancellationToken);

    Task HotOffersUpdated(IReadOnlyList<HotOffer> offers, CancellationToken cancellationToken);

    Task OrderDecided(string? connectionId, Guid orderId, string status, CancellationToken cancellationToken);

    Task KitchenUpdated(CancellationToken cancellationToken);
}

public sealed class NoopLiveNotifier : ILiveNotifier
{
    public Task StockChanged(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task BatchesExpiring(IReadOnlyCollection<Guid> batchIds, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task BatchesExpired(IReadOnlyCollection<Guid> batchIds, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopListUpdated(IReadOnlyList<ShiftTile> entries, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ShiftUpdated(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task HotOffersUpdated(IReadOnlyList<HotOffer> offers, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task OrderDecided(string? connectionId, Guid orderId, string status, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task KitchenUpdated(CancellationToken cancellationToken) => Task.CompletedTask;
}
