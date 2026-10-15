using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Monitoring.Core;
using Monitoring.Core.Inventory;
using Monitoring.Core.Time;
using Monitoring.Infrastructure.Persistence;

namespace Monitoring.Infrastructure.Services;

/// <summary>Приём заказа: проверка стопа и лимита, затем FEFO по складу и минус порции заготовки.</summary>
public sealed class OrderService(AppDbContext db, IClock clock, StockReadModel readModel, StockBroadcaster broadcaster, ShiftService shift)
{
    /// <summary>
    /// Вход: id, автор, строки, стол. Официант без стола — ошибка, склад не трогаем.
    /// Повтор того же id возвращает уже записанный заказ.
    /// Стоп или порций больше лимита — отказ 409, партии не меняются.
    /// Иначе списывает партии раньше срока и уменьшает prepPortions. Ноль порций ставит стоп OutOfProduct.
    /// </summary>
    public async Task<OrderPlacement> PlaceAsync(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        var replay = await readModel.OrderAsync(command.Id, cancellationToken);
        if (replay is not null)
        {
            return replay.Status == OrderStatus.Rejected
                ? OrderPlacement.Rejected(replay, false)
                : OrderPlacement.Accepted(replay, false);
        }

        if (command.Items.Count == 0)
        {
            return OrderPlacement.Invalid("В заказе нет блюд.");
        }

        if (command.RequireTable && string.IsNullOrWhiteSpace(command.TableNumber))
        {
            return OrderPlacement.Invalid("Укажите номер стола.");
        }

        var portions = command.Items
            .GroupBy(item => item.DishId)
            .Select(group => (DishId: group.Key, Portions: group.Sum(item => item.Portions)))
            .ToList();
        if (portions.Any(line => line.Portions <= 0))
        {
            return OrderPlacement.Invalid("Количество порций должно быть больше нуля.");
        }

        var dishIds = portions.Select(line => line.DishId).ToList();
        var dishes = await db.Dishes
            .Include(dish => dish.Ingredients)
            .ThenInclude(line => line.Product)
            .Where(dish => dishIds.Contains(dish.Id) && dish.IsActive)
            .ToListAsync(cancellationToken);
        if (dishes.Count != dishIds.Count)
        {
            return OrderPlacement.Invalid("Блюдо не найдено или снято с меню.");
        }

        var requirements = new List<ProductRequirement>();
        foreach (var line in portions)
        {
            var dish = dishes.Single(candidate => candidate.Id == line.DishId);
            if (dish.Ingredients.Count == 0)
            {
                return OrderPlacement.Invalid($"У блюда «{dish.Name}» нет рецепта.");
            }

            foreach (var ingredient in dish.Ingredients)
            {
                requirements.Add(new ProductRequirement(ingredient.ProductId, ingredient.Quantity * line.Portions));
            }
        }

        var productIds = requirements.Select(requirement => requirement.ProductId).Distinct().OrderBy(id => id).ToList();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        foreach (var productId in productIds)
        {
            await LockProductBatchesAsync(productId, cancellationToken);
        }

        var locked = await db.Batches
            .Where(batch => productIds.Contains(batch.ProductId) && batch.Status == BatchStatus.Active)
            .ToListAsync(cancellationToken);

        var now = clock.UtcNow;
        var order = new CustomerOrder
        {
            Id = command.Id,
            CreatedAt = now,
            CreatedBy = command.CreatedBy,
            TableNumber = string.IsNullOrWhiteSpace(command.TableNumber) ? null : command.TableNumber.Trim(),
            Items = portions.Select(line => new OrderItem
            {
                Id = Guid.NewGuid(),
                DishId = line.DishId,
                Portions = line.Portions,
                KitchenStatus = KitchenStatus.Queued,
            }).ToList(),
        };
        var shiftRows = await db.ShiftDishes.Where(row => dishIds.Contains(row.DishId)).ToListAsync(cancellationToken);
        var tiles = (await readModel.StopListAsync(cancellationToken)).ToDictionary(tile => tile.DishId);
        var blocks = new List<string>();
        foreach (var line in portions)
        {
            var state = shiftRows.FirstOrDefault(row => row.DishId == line.DishId);
            var name = dishes.Single(candidate => candidate.Id == line.DishId).Name;
            tiles.TryGetValue(line.DishId, out var tile);
            if (tile?.IsStopped == true)
            {
                var why = string.IsNullOrWhiteSpace(tile.Reason) ? null : tile.Reason;
                blocks.Add(why is null ? $"{name}: стоп" : $"{name}: стоп — {why}");
            }
            else if (state?.PrepPortions is int prep && line.Portions > prep)
            {
                blocks.Add($"{name}: на заготовке {prep}, в заказе {line.Portions}");
            }
        }

        if (blocks.Count > 0)
        {
            order.Status = OrderStatus.Rejected;
            order.RejectionReason = string.Join("; ", blocks);
            db.Orders.Add(order);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            var rejectedShift = await readModel.OrderAsync(order.Id, cancellationToken);
            return OrderPlacement.Rejected(rejectedShift!, true);
        }

        var outcome = FefoAllocator.TryAllocate(
            requirements,
            locked.Select(batch => new BatchSlice(
                batch.Id,
                batch.ProductId,
                batch.QuantityRemaining,
                batch.ExpiresAt,
                batch.ReceivedAt,
                batch.Status)).ToList(),
            now);

        if (!outcome.Success)
        {
            var names = await db.Products.Where(product => productIds.Contains(product.Id))
                .ToDictionaryAsync(product => product.Id, product => product.Name, cancellationToken);
            order.Status = OrderStatus.Rejected;
            order.Shortages = outcome.Shortages.Select(shortage => new OrderShortage
            {
                Id = Guid.NewGuid(),
                ProductId = shortage.ProductId,
                Required = shortage.Required,
                Available = shortage.Available,
            }).ToList();
            order.RejectionReason = string.Join("; ", outcome.Shortages.Select(shortage =>
                $"{names.GetValueOrDefault(shortage.ProductId, "Продукт")}: нужно {Format(shortage.Required)}, доступно {Format(shortage.Available)}, дефицит {Format(shortage.Deficit)}"));
            db.Orders.Add(order);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync(cancellationToken);
                db.ChangeTracker.Clear();
                var raced = await readModel.OrderAsync(command.Id, cancellationToken);
                if (raced is null)
                {
                    throw;
                }

                return raced.Status == OrderStatus.Rejected
                    ? OrderPlacement.Rejected(raced, false)
                    : OrderPlacement.Accepted(raced, false);
            }

            await broadcaster.PublishAsync(
                new StockChange([], [], [], order.Id, nameof(OrderStatus.Rejected), command.ConnectionId),
                cancellationToken);
            var rejected = await readModel.OrderAsync(order.Id, cancellationToken);
            return OrderPlacement.Rejected(rejected!, true);
        }

        var byId = locked.ToDictionary(batch => batch.Id);
        foreach (var allocation in outcome.Lines)
        {
            var batch = byId[allocation.BatchId];
            var before = batch.QuantityRemaining;
            batch.QuantityRemaining -= allocation.Quantity;
            if (batch.QuantityRemaining <= 0)
            {
                batch.QuantityRemaining = 0;
                batch.Status = BatchStatus.Depleted;
            }

            db.StockMovements.Add(new StockMovement
            {
                Id = Guid.NewGuid(),
                BatchId = batch.Id,
                OrderId = order.Id,
                Type = MovementType.OrderWriteOff,
                Quantity = allocation.Quantity,
                QuantityBefore = before,
                CreatedAt = now,
            });
        }

        order.Status = OrderStatus.Accepted;
        await shift.ApplyOrderPortionsAsync(portions.Select(line => (line.DishId, line.Portions)).ToList(), cancellationToken);
        db.Orders.Add(order);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            var raced = await readModel.OrderAsync(command.Id, cancellationToken);
            if (raced is null)
            {
                throw;
            }

            return raced.Status == OrderStatus.Rejected
                ? OrderPlacement.Rejected(raced, false)
                : OrderPlacement.Accepted(raced, false);
        }

        await broadcaster.PublishAsync(
            new StockChange(productIds, [], [], order.Id, nameof(OrderStatus.Accepted), command.ConnectionId),
            cancellationToken);
        var accepted = await readModel.OrderAsync(order.Id, cancellationToken);
        return OrderPlacement.Accepted(accepted!, true);
    }

    private async Task LockProductBatchesAsync(Guid productId, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = """
            SELECT id FROM batches
            WHERE product_id = @productId AND status = 'Active'
            FOR UPDATE
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "productId";
        parameter.Value = productId;
        command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
        }
    }

    private static string Format(decimal value) =>
        value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}
