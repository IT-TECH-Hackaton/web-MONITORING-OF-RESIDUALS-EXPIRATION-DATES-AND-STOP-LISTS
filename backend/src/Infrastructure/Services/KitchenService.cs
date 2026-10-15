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
public sealed class KitchenService(AppDbContext db, IClock clock, ILiveNotifier notifier, StockBroadcaster broadcaster, StaffService staff)
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

    /// <summary>
    /// Итог принятого заказа: готово, брак или отказ.
    /// Продукты уже списаны при отправке из зала. Брак и отказ помечают это списание и не списывают второй раз.
    /// Отказ требует заметку и зовёт официантов к столу.
    /// </summary>
    public async Task<ServiceResult<OrderView>> DecideAsync(
        Guid orderId,
        KitchenStatus result,
        string? note,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        if (result is not (KitchenStatus.Ready or KitchenStatus.Waste or KitchenStatus.Problem))
        {
            return ServiceResult<OrderView>.Bad("Отметка: готово, брак или отказ.");
        }

        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (result == KitchenStatus.Problem && note is null)
        {
            return ServiceResult<OrderView>.Bad("К отказу нужна заметка: что сказать гостю.");
        }

        if (note is { Length: > 300 })
        {
            return ServiceResult<OrderView>.Bad("Заметка кухни длиннее 300 символов.");
        }

        var order = await db.Orders
            .Include(row => row.Items).ThenInclude(item => item.Dish)
            .Include(row => row.Movements)
            .FirstOrDefaultAsync(row => row.Id == orderId, cancellationToken);
        if (order is null || order.Status != OrderStatus.Accepted)
        {
            return ServiceResult<OrderView>.Bad("Принятый заказ не найден.");
        }

        if (order.Items.Any(item => item.KitchenStatus is KitchenStatus.Ready or KitchenStatus.Waste or KitchenStatus.Problem))
        {
            return ServiceResult<OrderView>.Bad("Заказ уже отмечен.");
        }

        foreach (var item in order.Items)
        {
            item.KitchenStatus = result;
            item.KitchenNote = note;
        }

        if (result is KitchenStatus.Waste or KitchenStatus.Problem)
        {
            var reason = result == KitchenStatus.Waste ? nameof(WriteOffReason.Waste) : "Problem";
            foreach (var movement in order.Movements.Where(movement => movement.Type == MovementType.OrderWriteOff))
            {
                movement.Reason = reason;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        if (result == KitchenStatus.Problem)
        {
            var table = string.IsNullOrWhiteSpace(order.TableNumber) ? "без номера" : order.TableNumber.Trim();
            var dishes = string.Join(", ", order.Items.Select(item => $"{item.Dish.Name} × {item.Portions}"));
            var text = $"Стол {table}: кухня не отдаёт ({dishes}). {note}";
            if (text.Length > 500)
            {
                text = text[..500];
            }

            await staff.PostAsync(actorId, text, order.CreatedBy, false, cancellationToken);
            var waiters = await db.Users.AsNoTracking()
                .Where(user => user.Approved && user.Role == UserRole.Waiter)
                .Select(user => user.Id)
                .ToListAsync(cancellationToken);
            await notifier.TableProblem(waiters, table, dishes, note!, cancellationToken);
        }

        await notifier.KitchenUpdated(cancellationToken);
        var view = await db.Orders.AsNoTracking()
            .Include(row => row.CreatedByUser)
            .Include(row => row.Items).ThenInclude(line => line.Dish)
            .Include(row => row.Shortages).ThenInclude(shortage => shortage.Product)
            .Include(row => row.Movements).ThenInclude(movement => movement.Batch).ThenInclude(batch => batch.Product)
            .FirstAsync(row => row.Id == orderId, cancellationToken);
        return ServiceResult<OrderView>.Ok(StockReadModel.MapOrder(view));
    }

    /// <summary>Ставит позиции заказа Queued, Cooking или Ready и заметку. Новый экран смены не вызывает.</summary>
    public async Task<ServiceResult<OrderView>> MarkDishAsync(
        Guid orderId,
        Guid itemId,
        KitchenStatus status,
        string? note,
        CancellationToken cancellationToken)
    {
        var item = await db.OrderItems.Include(line => line.Order).FirstOrDefaultAsync(
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

        item.KitchenStatus = status;
        item.KitchenNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        await db.SaveChangesAsync(cancellationToken);
        await notifier.KitchenUpdated(cancellationToken);
        var order = await db.Orders.AsNoTracking()
            .Include(row => row.CreatedByUser)
            .Include(row => row.Items).ThenInclude(line => line.Dish)
            .Include(row => row.Shortages).ThenInclude(shortage => shortage.Product)
            .FirstAsync(row => row.Id == orderId, cancellationToken);
        return ServiceResult<OrderView>.Ok(StockReadModel.MapOrder(order));
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
