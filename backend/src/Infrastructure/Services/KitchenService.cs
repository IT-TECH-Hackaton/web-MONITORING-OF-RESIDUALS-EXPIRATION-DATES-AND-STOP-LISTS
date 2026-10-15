using Microsoft.EntityFrameworkCore;
using Monitoring.Core;
using Monitoring.Core.Live;
using Monitoring.Core.Time;
using Monitoring.Infrastructure.Persistence;

namespace Monitoring.Infrastructure.Services;

public sealed record KitchenBatchCard(
    Guid BatchId,
    string ProductName,
    Unit Unit,
    string DeliveryNumber,
    decimal Remaining,
    double HoursLeft,
    DateTime? OpenedAt,
    string? Note);

/// <summary>Доска «использовать первым»: только партии. Очереди заказов в ответе нет.</summary>
public sealed record KitchenBoard(IReadOnlyList<KitchenBatchCard> Batches);

/// <summary>Короткий срок, вскрытие партии и старые отметки тикета.</summary>
public sealed class KitchenService(AppDbContext db, IClock clock, ILiveNotifier notifier, StockBroadcaster broadcaster, OrderService orders)
{
    /// <summary>Активные партии, которые истекают за 72 часа или уже вскрыты. До 40 штук.</summary>
    public async Task<KitchenBoard> BoardAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var soon = now.AddHours(72);
        var batches = await db.Batches.AsNoTracking()
            .Include(batch => batch.Product)
            .Include(batch => batch.Delivery)
            .Where(batch => batch.Status == BatchStatus.Active && batch.QuantityRemaining > 0 && (batch.ExpiresAt <= soon || batch.OpenedAt != null))
            .OrderBy(batch => batch.ExpiresAt)
            .Take(40)
            .ToListAsync(cancellationToken);

        return new KitchenBoard(
            batches.Select(batch => new KitchenBatchCard(
                batch.Id,
                batch.Product.Name,
                batch.Product.Unit,
                batch.Delivery.Number,
                batch.QuantityRemaining,
                FreshnessRules.HoursLeft(batch.ExpiresAt, now),
                batch.OpenedAt,
                batch.KitchenNote)).ToList());
    }

    /// <summary>Ставит позиции заказа Queued, Cooking или Ready и заметку. Новый экран смены не вызывает.</summary>
    public async Task<ServiceResult<OrderView>> MarkDishAsync(
        Guid orderId,
        Guid itemId,
        KitchenStatus status,
        string? note,
        CancellationToken cancellationToken)
    {
        var item = await db.OrderItems.Include(line => line.Order).Include(line => line.Dish).FirstOrDefaultAsync(
            line => line.Id == itemId && line.OrderId == orderId,
            cancellationToken);
        if (item is null || item.Order.Status != OrderStatus.Accepted)
        {
            return ServiceResult<OrderView>.Bad("Блюдо в принятом заказе не найдено.");
        }

        if (note is { Length: > 300 })
        {
            return ServiceResult<OrderView>.Bad("Заметка кухни длиннее 300 символов.");
        }

        if (status == KitchenStatus.Spoiled)
        {
            if (string.IsNullOrWhiteSpace(note))
            {
                return ServiceResult<OrderView>.Bad("Напишите, что сказать официанту.");
            }

            if (item.KitchenStatus == KitchenStatus.Spoiled)
            {
                return ServiceResult<OrderView>.Bad("Это блюдо уже отмечено испорченным.");
            }

            item.KitchenStatus = KitchenStatus.Spoiled;
            item.KitchenNote = note.Trim();
            await db.SaveChangesAsync(cancellationToken);
            var table = string.IsNullOrWhiteSpace(item.Order.TableNumber) ? "без стола" : $"стол {item.Order.TableNumber}";
            await notifier.OrderSpoiled(item.Order.CreatedBy, table, item.Dish?.Name ?? "Блюдо", item.KitchenNote, cancellationToken);
            await notifier.KitchenUpdated(cancellationToken);
            return ServiceResult<OrderView>.Ok(await LoadOrderAsync(orderId, cancellationToken));
        }

        if (status == KitchenStatus.Ready && item.KitchenStatus == KitchenStatus.Queued)
        {
            return ServiceResult<OrderView>.Bad("Сначала возьмите блюдо в работу.");
        }

        if (status == KitchenStatus.Cooking && item.KitchenStatus == KitchenStatus.Queued)
        {
            var cooked = await orders.CookItemAsync(orderId, itemId, cancellationToken);
            if (cooked.Error is not null)
            {
                return ServiceResult<OrderView>.Bad(cooked.Error);
            }

            item = await db.OrderItems.Include(line => line.Order).FirstAsync(
                line => line.Id == itemId && line.OrderId == orderId,
                cancellationToken);
        }

        item.KitchenStatus = status;
        item.KitchenNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        await db.SaveChangesAsync(cancellationToken);
        await notifier.KitchenUpdated(cancellationToken);
        return ServiceResult<OrderView>.Ok(await LoadOrderAsync(orderId, cancellationToken));
    }

    private async Task<OrderView> LoadOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await db.Orders.AsNoTracking()
            .Include(row => row.CreatedByUser)
            .Include(row => row.Items).ThenInclude(line => line.Dish).ThenInclude(dish => dish.Ingredients).ThenInclude(ingredient => ingredient.Product)
            .Include(row => row.Shortages).ThenInclude(shortage => shortage.Product)
            .Include(row => row.Movements).ThenInclude(movement => movement.Batch).ThenInclude(batch => batch.Product)
            .FirstAsync(row => row.Id == orderId, cancellationToken);
        return StockReadModel.MapOrder(order);
    }

    /// <summary>Вскрывает партию и пишет заметку. Количество списания больше нуля уменьшает остаток партии.</summary>
    public async Task<ServiceResult<KitchenBatchCard>> MarkBatchAsync(
        Guid batchId,
        bool opened,
        string? note,
        decimal? writeOffQuantity,
        CancellationToken cancellationToken)
    {
        var batch = await db.Batches.Include(row => row.Product).Include(row => row.Delivery)
            .FirstOrDefaultAsync(row => row.Id == batchId, cancellationToken);
        if (batch is null || batch.Status != BatchStatus.Active)
        {
            return ServiceResult<KitchenBatchCard>.Bad("Активная партия не найдена.");
        }

        if (opened && batch.OpenedAt is null)
        {
            batch.OpenedAt = clock.UtcNow;
            var openedUntil = batch.OpenedAt.Value.AddHours(24);
            if (openedUntil < batch.ExpiresAt)
            {
                batch.ExpiresAt = openedUntil;
            }
        }

        if (note is { Length: > 300 })
        {
            return ServiceResult<KitchenBatchCard>.Bad("Заметка кухни длиннее 300 символов.");
        }

        batch.KitchenNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        if (writeOffQuantity is > 0)
        {
            if (writeOffQuantity > batch.QuantityRemaining)
            {
                return ServiceResult<KitchenBatchCard>.Bad("Нельзя списать больше, чем осталось в партии.");
            }

            batch.QuantityRemaining -= writeOffQuantity.Value;
            if (batch.QuantityRemaining == 0)
            {
                batch.Status = BatchStatus.Depleted;
            }

            db.StockMovements.Add(new StockMovement
            {
                Id = Guid.NewGuid(),
                BatchId = batch.Id,
                Type = MovementType.KitchenWriteOff,
                Quantity = writeOffQuantity.Value,
                CreatedAt = clock.UtcNow,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        if (writeOffQuantity is > 0)
        {
            await broadcaster.PublishAsync(new StockChange([batch.ProductId], [], [], null, null, null), cancellationToken);
        }
        else
        {
            await notifier.KitchenUpdated(cancellationToken);
        }

        var now = clock.UtcNow;
        return ServiceResult<KitchenBatchCard>.Ok(new KitchenBatchCard(
            batch.Id,
            batch.Product.Name,
            batch.Product.Unit,
            batch.Delivery.Number,
            batch.QuantityRemaining,
            FreshnessRules.HoursLeft(batch.ExpiresAt, now),
            batch.OpenedAt,
            batch.KitchenNote));
    }
}
