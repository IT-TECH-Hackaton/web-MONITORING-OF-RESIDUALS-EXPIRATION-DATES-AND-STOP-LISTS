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

/// <summary>Фоновый проход срока. Сам вызывается воркером, не ручкой.</summary>
public sealed class ExpiryProcessor(AppDbContext db, IClock clock, StockBroadcaster broadcaster, ExpiryWatchState state, StaffService staff)
{
    /// <summary>
    /// Просроченную активную партию ставит под списание. Остаток не обнуляет: списывает человек.
    /// Предупреждает о тех, что истекают в ближайшие сутки. Повторно уже объявленную партию не орёт.
    /// </summary>
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var horizon = now.AddHours(FreshnessRules.WarningHours);
        var expired = await db.Batches
            .Include(batch => batch.Product)
            .Where(batch => batch.Status == BatchStatus.Active && batch.QuantityRemaining > 0 && batch.ExpiresAt <= now)
            .ToListAsync(cancellationToken);

        var notes = new List<StaffMessage>();
        foreach (var batch in expired)
        {
            batch.Status = BatchStatus.PendingWriteOff;
            state.Forget(batch.Id);
            notes.Add(staff.StageSystem($"{batch.Product.Name}, {Qty(batch.QuantityRemaining, batch.Product.Unit)}: порча. Срок вышел, с готовки снят."));
        }

        if (expired.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            foreach (var note in notes)
            {
                await staff.AnnounceAsync(note, cancellationToken);
            }
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

    private static string Qty(decimal quantity, Unit unit)
    {
        var label = unit switch
        {
            Unit.G => "г",
            Unit.Ml => "мл",
            _ => "шт",
        };
        return $"{quantity:0.###} {label}";
    }
}
