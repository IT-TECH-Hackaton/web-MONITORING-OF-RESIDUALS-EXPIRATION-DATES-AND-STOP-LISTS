using Monitoring.Core.Live;

namespace Monitoring.Infrastructure.Services;

/// <summary>Что изменилось за одну операцию. Пустые списки всё равно обновляют стоп, подсказки и смену.</summary>
public sealed record StockChange(
    IReadOnlyCollection<Guid> ProductIds,
    IReadOnlyCollection<Guid> ExpiringBatchIds,
    IReadOnlyCollection<Guid> ExpiredBatchIds,
    Guid? OrderId,
    string? OrderStatus,
    string? ConnectionId);

/// <summary>После записи в базу рассылает живые события. Перечитывает стоп-лист и подсказки уже из базы.</summary>
public sealed class StockBroadcaster(StockReadModel readModel, ILiveNotifier notifier)
{
    /// <summary>Вход: какие продукты и партии задеты и, если был заказ, его id и статус. Выход нет: только SignalR.</summary>
    public async Task PublishAsync(StockChange change, CancellationToken cancellationToken)
    {
        if (change.ProductIds.Count > 0)
        {
            await notifier.StockChanged(change.ProductIds, cancellationToken);
        }

        if (change.ExpiringBatchIds.Count > 0)
        {
            await notifier.BatchesExpiring(change.ExpiringBatchIds, cancellationToken);
        }

        if (change.ExpiredBatchIds.Count > 0)
        {
            await notifier.BatchesExpired(change.ExpiredBatchIds, cancellationToken);
        }

        await notifier.StopListUpdated(await readModel.StopListAsync(cancellationToken), cancellationToken);
        await notifier.HotOffersUpdated(await readModel.HotOffersAsync(cancellationToken), cancellationToken);
        await notifier.ShiftUpdated(cancellationToken);

        if (change.OrderId is Guid orderId && change.OrderStatus is not null)
        {
            await notifier.OrderDecided(change.ConnectionId, orderId, change.OrderStatus, cancellationToken);
        }
    }
}
