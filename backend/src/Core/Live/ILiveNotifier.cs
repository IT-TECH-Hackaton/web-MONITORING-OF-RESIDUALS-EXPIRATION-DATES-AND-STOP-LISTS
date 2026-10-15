using Monitoring.Core.Inventory;

namespace Monitoring.Core.Live;

/// <summary>Партия для живого текста: что протухает или уже под списание.</summary>
public sealed record LiveBatchNotice(Guid BatchId, string ProductName, decimal Quantity, Guid ProductId);

/// <summary>
/// События в группу SignalR live. Клиенты по ним перечитывают экраны.
/// stoplist.updated несёт плитки смены, shift.updated — пустой сигнал «смена изменилась»,
/// order.decided уходит только соединению, которое создало заказ.
/// </summary>
public interface ILiveNotifier
{
    Task StockChanged(IReadOnlyCollection<Guid> productIds, IReadOnlyCollection<string> depletedNames, CancellationToken cancellationToken);

    Task BatchesExpiring(IReadOnlyList<LiveBatchNotice> batches, CancellationToken cancellationToken);

    Task BatchesExpired(IReadOnlyList<LiveBatchNotice> batches, CancellationToken cancellationToken);

    Task StopListUpdated(IReadOnlyList<ShiftTile> entries, CancellationToken cancellationToken);

    Task ShiftUpdated(CancellationToken cancellationToken);

    Task HotOffersUpdated(IReadOnlyList<HotOffer> offers, CancellationToken cancellationToken);

    Task OrderDecided(string? connectionId, Guid orderId, string status, CancellationToken cancellationToken);

    Task KitchenUpdated(CancellationToken cancellationToken);

    Task ChatPosted(Guid id, string author, string role, string text, DateTime at, string channel, Guid? authorId, Guid? recipientId, CancellationToken cancellationToken);

    /// <summary>Только перечисленным людям. Чат с поставщиком не уходит в общую группу.</summary>
    Task ChatAddressed(IReadOnlyCollection<Guid> userIds, Guid id, string author, string role, string text, DateTime at, string channel, Guid? authorId, CancellationToken cancellationToken);
}

public sealed class NoopLiveNotifier : ILiveNotifier
{
    public Task StockChanged(IReadOnlyCollection<Guid> productIds, IReadOnlyCollection<string> depletedNames, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task BatchesExpiring(IReadOnlyList<LiveBatchNotice> batches, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task BatchesExpired(IReadOnlyList<LiveBatchNotice> batches, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopListUpdated(IReadOnlyList<ShiftTile> entries, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ShiftUpdated(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task HotOffersUpdated(IReadOnlyList<HotOffer> offers, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task OrderDecided(string? connectionId, Guid orderId, string status, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task KitchenUpdated(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ChatPosted(Guid id, string author, string role, string text, DateTime at, string channel, Guid? authorId, Guid? recipientId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ChatAddressed(IReadOnlyCollection<Guid> userIds, Guid id, string author, string role, string text, DateTime at, string channel, Guid? authorId, CancellationToken cancellationToken) => Task.CompletedTask;
}
