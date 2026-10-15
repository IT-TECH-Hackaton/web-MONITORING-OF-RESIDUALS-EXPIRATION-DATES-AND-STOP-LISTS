using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Monitoring.Core;
using Monitoring.Core.Inventory;
using Monitoring.Core.Time;
using Monitoring.Infrastructure.Persistence;

namespace Monitoring.Infrastructure.Services;

public sealed record WriteOffLine(Guid ProductId, decimal Quantity, Guid? BatchId);

/// <summary>Акт списания сырья. Порции заготовки не трогает.</summary>
public sealed class WriteOffService(AppDbContext db, IClock clock, StockBroadcaster broadcaster, StaffService staff)
{
    /// <summary>
    /// Вход: статья и строки (продукт, количество, необязательная партия).
    /// Без партии берёт FEFO. С партией списывает только её и не больше остатка.
    /// Выход: число строк или текст ошибки. При ошибке склад откатывается.
    /// </summary>
    public async Task<ServiceResult<int>> ActAsync(
        string reason,
        IReadOnlyList<WriteOffLine> items,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<WriteOffReason>(reason, true, out var parsed))
        {
            return ServiceResult<int>.Bad("Причина: Expiry, Waste, Spoilage, Training или StaffMeal.");
        }

        if (items.Count == 0 || items.Any(item => item.Quantity <= 0))
        {
            return ServiceResult<int>.Bad("В акте нет количества больше нуля.");
        }

        var movementType = parsed == WriteOffReason.Expiry ? MovementType.ExpiryWriteOff : MovementType.KitchenWriteOff;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var touched = new HashSet<Guid>();
        foreach (var item in items.OrderBy(line => line.ProductId))
        {
            if (item.BatchId is Guid batchId)
            {
                await LockBatchAsync(batchId, cancellationToken);
                var batch = await db.Batches.FirstOrDefaultAsync(row => row.Id == batchId && row.ProductId == item.ProductId, cancellationToken);
                var held = batch is not null && batch.Status == BatchStatus.PendingWriteOff && parsed == WriteOffReason.Expiry;
                if (batch is null || (!held && (batch.Status != BatchStatus.Active || batch.ExpiresAt <= clock.UtcNow)))
                {
                    return ServiceResult<int>.Bad("Партия для списания не найдена или уже не активна.");
                }

                if (item.Quantity > batch.QuantityRemaining)
                {
                    return ServiceResult<int>.Bad("Нельзя списать больше, чем осталось в партии.");
                }

                Deduct(batch, item.Quantity, movementType, parsed);
                touched.Add(batch.ProductId);
                continue;
            }

            await LockProductAsync(item.ProductId, cancellationToken);
            var batches = await db.Batches
                .Where(row => row.ProductId == item.ProductId && row.Status == BatchStatus.Active)
                .ToListAsync(cancellationToken);
            var outcome = FefoAllocator.TryAllocate(
                [new ProductRequirement(item.ProductId, item.Quantity)],
                batches.Select(row => new BatchSlice(row.Id, row.ProductId, row.QuantityRemaining, row.ExpiresAt, row.ReceivedAt, row.Status)).ToList(),
                clock.UtcNow);
            if (!outcome.Success)
            {
                return ServiceResult<int>.Bad("На складе не хватает продукта для акта списания.");
            }

            var byId = batches.ToDictionary(row => row.Id);
            foreach (var line in outcome.Lines)
            {
                Deduct(byId[line.BatchId], line.Quantity, movementType, parsed);
            }

            touched.Add(item.ProductId);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await broadcaster.PublishAsync(new StockChange(touched, [], [], null, null, null), cancellationToken);
        if (parsed == WriteOffReason.Spoilage)
        {
            var ids = items.Select(item => item.ProductId).Distinct().ToList();
            var products = await db.Products.AsNoTracking()
                .Where(product => ids.Contains(product.Id))
                .ToDictionaryAsync(product => product.Id, cancellationToken);
            foreach (var item in items)
            {
                products.TryGetValue(item.ProductId, out var product);
                var name = product?.Name ?? "Продукт";
                var unit = product?.Unit ?? Unit.Pcs;
                await staff.PostSystemAsync($"{name}, {Qty(item.Quantity, unit)}: порча, списано со склада.", cancellationToken);
            }
        }

        return ServiceResult<int>.Ok(items.Count);
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

    private void Deduct(Batch batch, decimal quantity, MovementType type, WriteOffReason reason)
    {
        var before = batch.QuantityRemaining;
        batch.QuantityRemaining -= quantity;
        if (batch.QuantityRemaining <= 0)
        {
            batch.QuantityRemaining = 0;
            batch.Status = batch.Status == BatchStatus.PendingWriteOff || reason == WriteOffReason.Expiry
                ? BatchStatus.Expired
                : BatchStatus.Depleted;
        }

        db.StockMovements.Add(new StockMovement
        {
            Id = Guid.NewGuid(),
            BatchId = batch.Id,
            Type = type,
            Reason = reason.ToString(),
            Quantity = quantity,
            QuantityBefore = before,
            CreatedAt = clock.UtcNow,
        });
    }

    private async Task LockProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = """SELECT id FROM batches WHERE product_id = @productId AND status = 'Active' FOR UPDATE""";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "productId";
        parameter.Value = productId;
        command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
        }
    }

    private async Task LockBatchAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = """SELECT id FROM batches WHERE id = @batchId FOR UPDATE""";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "batchId";
        parameter.Value = batchId;
        command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
        }
    }
}
