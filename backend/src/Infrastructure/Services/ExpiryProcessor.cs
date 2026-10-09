using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Monitoring.Core;
using Monitoring.Core.Time;
using Monitoring.Infrastructure.Persistence;

namespace Monitoring.Infrastructure.Services;

public sealed class ExpiryWatchState
{
    private readonly ConcurrentDictionary<Guid, byte> _announced = new();

    public bool TryAnnounce(Guid batchId) => _announced.TryAdd(batchId, 0);

    public void Forget(Guid batchId) => _announced.TryRemove(batchId, out _);
}

public sealed class ExpiryProcessor(AppDbContext db, IClock clock, StockBroadcaster broadcaster, ExpiryWatchState state)
{
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var horizon = now.AddHours(FreshnessRules.WarningHours);
        var expired = await db.Batches
            .Where(batch => batch.Status == BatchStatus.Active && batch.QuantityRemaining > 0 && batch.ExpiresAt <= now)
            .ToListAsync(cancellationToken);

        foreach (var batch in expired)
        {
            db.StockMovements.Add(new StockMovement
            {
                Id = Guid.NewGuid(),
                BatchId = batch.Id,
                Type = MovementType.ExpiryWriteOff,
                Quantity = batch.QuantityRemaining,
                CreatedAt = now,
            });
            batch.QuantityRemaining = 0;
            batch.Status = BatchStatus.Expired;
            state.Forget(batch.Id);
        }

        if (expired.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        var expiringIds = await db.Batches
            .Where(batch => batch.Status == BatchStatus.Active
                && batch.QuantityRemaining > 0
                && batch.ExpiresAt > now
                && batch.ExpiresAt <= horizon)
            .Select(batch => batch.Id)
            .ToListAsync(cancellationToken);
        var freshWarnings = expiringIds.Where(state.TryAnnounce).ToList();

        if (expired.Count == 0 && freshWarnings.Count == 0)
        {
            return;
        }

        await broadcaster.PublishAsync(
            new StockChange(
                expired.Select(batch => batch.ProductId).Distinct().ToList(),
                freshWarnings,
                expired.Select(batch => batch.Id).ToList(),
                null,
                null,
                null),
            cancellationToken);
    }
}
