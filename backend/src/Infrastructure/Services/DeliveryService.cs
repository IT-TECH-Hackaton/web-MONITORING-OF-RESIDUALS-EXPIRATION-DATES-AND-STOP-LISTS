using Microsoft.EntityFrameworkCore;
using Monitoring.Core;
using Monitoring.Core.Time;
using Monitoring.Infrastructure.Persistence;

namespace Monitoring.Infrastructure.Services;

/// <summary>Приёмка поставки в партии. Повтор того же id возвращает уже созданную, склад второй раз не растёт.</summary>
public sealed class DeliveryService(AppDbContext db, IClock clock, StockBroadcaster broadcaster)
{
    /// <summary>Вход: номер, строки с количеством и сроком. Выход: поставка и флаг, создана она сейчас или уже была.</summary>
    public async Task<ServiceResult<DeliveryReceipt>> ReceiveAsync(ReceiveDeliveryCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Number))
        {
            return ServiceResult<DeliveryReceipt>.Bad("Укажите номер поставки.");
        }

        if (command.Items.Count == 0)
        {
            return ServiceResult<DeliveryReceipt>.Bad("В поставке нет продуктов.");
        }

        var existing = await db.Deliveries.AsNoTracking().FirstOrDefaultAsync(delivery => delivery.Id == command.Id, cancellationToken);
        if (existing is not null)
        {
            var stored = await GetAsync(existing.Id, cancellationToken);
            return ServiceResult<DeliveryReceipt>.Ok(new DeliveryReceipt(stored!, false));
        }

        var receivedAt = SpecifyUtc(command.CreatedAt ?? clock.UtcNow);
        var batches = new List<Batch>();
        var productIds = new HashSet<Guid>();

        foreach (var item in command.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
            {
                return ServiceResult<DeliveryReceipt>.Bad("У продукта должно быть название.");
            }

            if (item.Quantity <= 0)
            {
                return ServiceResult<DeliveryReceipt>.Bad("Количество в поставке должно быть больше нуля.");
            }

            if (item.ExpiresAt is null && item.ShelfLifeHours is not > 0)
            {
                return ServiceResult<DeliveryReceipt>.Bad("Укажите срок годности или срок хранения в часах.");
            }

            var expiresAt = SpecifyUtc(item.ExpiresAt ?? receivedAt.AddHours(item.ShelfLifeHours!.Value));
            if (expiresAt <= receivedAt)
            {
                return ServiceResult<DeliveryReceipt>.Bad("Срок годности должен быть позже даты поставки.");
            }

            var product = await db.Products.FirstOrDefaultAsync(candidate => candidate.Id == item.ProductId, cancellationToken);
            if (product is null)
            {
                product = new Product
                {
                    Id = item.ProductId,
                    Name = item.Name.Trim(),
                    Unit = item.Unit,
                    IsActive = true,
                };
                db.Products.Add(product);
            }

            productIds.Add(product.Id);
            var batch = new Batch
            {
                Id = Guid.NewGuid(),
                DeliveryId = command.Id,
                ProductId = product.Id,
                QuantityInitial = item.Quantity,
                QuantityRemaining = item.Quantity,
                ReceivedAt = receivedAt,
                ExpiresAt = expiresAt,
                Status = BatchStatus.Active,
                UnitCost = product.PurchasePrice,
                CertificateNumber = item.Certificate?.Number.Trim(),
                CertificateIssuer = item.Certificate?.Issuer.Trim(),
                CertificateIssuedAt = item.Certificate is null ? null : SpecifyUtc(item.Certificate.IssuedAt),
                CertificateExpiresAt = item.Certificate is null ? null : SpecifyUtc(item.Certificate.ExpiresAt),
            };
            batches.Add(batch);
            db.StockMovements.Add(new StockMovement
            {
                Id = Guid.NewGuid(),
                Batch = batch,
                Type = MovementType.Receipt,
                Quantity = item.Quantity,
                CreatedAt = receivedAt,
            });
        }

        db.Deliveries.Add(new Delivery
        {
            Id = command.Id,
            Number = command.Number.Trim(),
            Supplier = string.IsNullOrWhiteSpace(command.Supplier) ? null : command.Supplier.Trim(),
            CreatedAt = receivedAt,
            ReceivedBy = command.ReceivedBy,
            Batches = batches,
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var stored = await GetAsync(command.Id, cancellationToken);
            if (stored is null)
            {
                throw;
            }

            return ServiceResult<DeliveryReceipt>.Ok(new DeliveryReceipt(stored, false));
        }

        await broadcaster.PublishAsync(new StockChange(productIds, [], [], null, null, null), cancellationToken);
        var created = await GetAsync(command.Id, cancellationToken);
        return ServiceResult<DeliveryReceipt>.Ok(new DeliveryReceipt(created!, true));
    }

    public async Task<IReadOnlyList<DeliveryView>> ListAsync(CancellationToken cancellationToken)
    {
        var deliveries = await db.Deliveries.AsNoTracking()
            .OrderByDescending(delivery => delivery.CreatedAt)
            .ToListAsync(cancellationToken);
        var result = new List<DeliveryView>();
        foreach (var delivery in deliveries)
        {
            var view = await GetAsync(delivery.Id, cancellationToken);
            if (view is not null)
            {
                result.Add(view);
            }
        }

        return result;
    }

    public async Task<DeliveryView?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var delivery = await db.Deliveries.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (delivery is null)
        {
            return null;
        }

        var own = await db.Batches.AsNoTracking()
            .Include(batch => batch.Product)
            .Include(batch => batch.Delivery)
            .Where(batch => batch.DeliveryId == id)
            .OrderBy(batch => batch.ExpiresAt)
            .ToListAsync(cancellationToken);
        return new DeliveryView(delivery.Id, delivery.Number, delivery.CreatedAt, own.Select(Map).ToList(), delivery.Supplier);
    }

    private BatchView Map(Batch batch)
    {
        var now = clock.UtcNow;
        return new BatchView(
            batch.Id,
            batch.DeliveryId,
            batch.Delivery.Number,
            batch.ProductId,
            batch.Product.Name,
            batch.Product.Unit,
            batch.QuantityInitial,
            batch.QuantityRemaining,
            batch.ReceivedAt,
            batch.ExpiresAt,
            FreshnessRules.HoursLeft(batch.ExpiresAt, now),
            batch.Status,
            FreshnessRules.ForBatch(batch.Status, batch.ExpiresAt, now),
            batch.CertificateNumber,
            batch.CertificateIssuer,
            batch.CertificateExpiresAt);
    }

    private static DateTime SpecifyUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
