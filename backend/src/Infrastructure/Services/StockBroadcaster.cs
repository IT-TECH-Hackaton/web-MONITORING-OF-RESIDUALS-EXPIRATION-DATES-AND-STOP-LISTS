using Monitoring.Core.Live;

namespace Monitoring.Infrastructure.Services;

public sealed record StockChange(
    IReadOnlyCollection<Guid> ProductIds,
    IReadOnlyCollection<Guid> ExpiringBatchIds,
    IReadOnlyCollection<Guid> ExpiredBatchIds,
    Guid? OrderId,
    string? OrderStatus,
    string? ConnectionId);

public sealed class StockBroadcaster(StockReadModel readModel, ILiveNotifier notifier)
{
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

        if (change.OrderId is Guid orderId && change.OrderStatus is not null)
        {
            await notifier.OrderDecided(change.ConnectionId, orderId, change.OrderStatus, cancellationToken);
        }
    }
}
