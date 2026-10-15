using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Monitoring.Core;
using Monitoring.Core.Time;
using Monitoring.Infrastructure.Persistence;

namespace Monitoring.Infrastructure.Services;

/// <summary>Приёмка поставки в партии. Повтор того же id возвращает уже созданную, склад второй раз не растёт.</summary>
public sealed class DeliveryService(AppDbContext db, IClock clock, StockBroadcaster broadcaster, PurchaseService purchases)
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

            if (item.Quantity > 1_000_000_000m)
            {
                return ServiceResult<DeliveryReceipt>.Bad("Количество слишком большое.");
            }

            if (item.ExpiresAt is null && item.ShelfLifeHours is not > 0)
            {
                return ServiceResult<DeliveryReceipt>.Bad("Укажите срок годности или срок хранения в часах.");
            }

            var expiresAt = item.ExpiresAt is DateTime dated && dated.TimeOfDay == TimeSpan.Zero
                ? EndOfKitchenDay(dated)
                : SpecifyUtc(item.ExpiresAt ?? receivedAt.AddHours(item.ShelfLifeHours!.Value));
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
                Status = command.Park ? BatchStatus.Arrived : BatchStatus.Active,
                UnitCost = product.PurchasePrice,
                CertificateNumber = item.Certificate?.Number.Trim(),
                CertificateIssuer = item.Certificate?.Issuer.Trim(),
                CertificateIssuedAt = item.Certificate is null ? null : SpecifyUtc(item.Certificate.IssuedAt),
                CertificateExpiresAt = item.Certificate is null ? null : SpecifyUtc(item.Certificate.ExpiresAt),
            };
            batches.Add(batch);
            if (!command.Park)
            {
                db.StockMovements.Add(new StockMovement
                {
                    Id = Guid.NewGuid(),
                    Batch = batch,
                    Type = MovementType.Receipt,
                    Quantity = item.Quantity,
                    CreatedAt = receivedAt,
                });
            }
        }

        db.Deliveries.Add(new Delivery
        {
            Id = command.Id,
            Number = command.Number.Trim(),
            Supplier = string.IsNullOrWhiteSpace(command.Supplier) ? null : command.Supplier.Trim(),
            CreatedAt = receivedAt,
            AcceptedAt = command.Park ? null : receivedAt,
            Stage = command.Park ? DeliveryStage.Parked : DeliveryStage.Accepted,
            ReceivedBy = command.Park ? null : command.ReceivedBy,
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

        if (!command.Park)
        {
            await purchases.FulfillForDeliveryAsync(command.Id, productIds, receivedAt, cancellationToken);
            await broadcaster.PublishAsync(new StockChange(productIds, [], [], null, null, null), cancellationToken);
        }
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
        return new DeliveryView(delivery.Id, delivery.Number, delivery.CreatedAt, own.Select(Map).ToList(), delivery.Supplier, delivery.AcceptedAt, delivery.Stage);
    }

    /// <summary>Открытый заказ поставщику становится одной поставкой «приехала». Склад не растёт, строки заказа закрываются.</summary>
    public static readonly DateTime PackDateMissing = new(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public async Task<ServiceResult<DeliveryView>> SendFromOrderAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockOpenPurchaseLinesAsync(cancellationToken);
        var open = await db.PurchaseLines
            .Include(line => line.Product)
            .Where(line => line.FulfilledAt == null)
            .OrderBy(line => line.CreatedAt)
            .ToListAsync(cancellationToken);
        if (open.Count == 0)
        {
            return ServiceResult<DeliveryView>.Bad("Открытого заказа нет.");
        }

        var now = clock.UtcNow;
        var id = Guid.NewGuid();
        var batches = open.Select(line => new Batch
        {
            Id = Guid.NewGuid(),
            DeliveryId = id,
            ProductId = line.ProductId,
            QuantityInitial = line.Quantity,
            QuantityRemaining = line.Quantity,
            UnitCost = line.Product.PurchasePrice,
            ReceivedAt = now,
            ExpiresAt = PackDateMissing,
            Status = BatchStatus.Arrived,
        }).ToList();
        foreach (var line in open)
        {
            line.FulfilledAt = now;
            line.FulfilledByDeliveryId = id;
        }

        db.Deliveries.Add(new Delivery
        {
            Id = id,
            Number = $"ЗАК-{now:yyyyMMdd-HHmmss}",
            Supplier = "По заказу",
            CreatedAt = now,
            Stage = DeliveryStage.Parked,
            Batches = batches,
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ServiceResult<DeliveryView>.Ok((await GetAsync(id, cancellationToken))!);
    }

    /// <summary>Фиксированная закупка, которую поставщик предлагает заведению. Склад не трогает.</summary>
    public async Task<IReadOnlyList<SupplyOfferLine>> StandardOfferAsync(CancellationToken cancellationToken)
    {
        var products = await CatalogAsync(cancellationToken);
        var today = DateOnly.FromDateTime(clock.UtcNow);
        return products
            .Select(product => new SupplyOfferLine(
                product.Id,
                product.Name,
                product.Unit,
                StandingQuantity(product.Name, product.Unit),
                EndOfKitchenDay(today.AddDays(StandingDays(product.Name)).ToDateTime(TimeOnly.MinValue))))
            .ToList();
    }

    /// <summary>Предложение уходит менеджеру как машина «приехала». Срок — пример, кухня поправит на приёмке. Склад не растёт.</summary>
    public async Task<ServiceResult<DeliveryView>> SendProposalAsync(IReadOnlyList<(Guid ProductId, decimal Quantity, DateTime ExpiresAt)> lines, CancellationToken cancellationToken)
    {
        var kept = lines.Where(line => line.Quantity > 0).ToList();
        if (kept.Count == 0)
        {
            return ServiceResult<DeliveryView>.Bad("В предложении не осталось позиций.");
        }

        var now = clock.UtcNow;
        var batches = new List<Batch>();
        var id = Guid.NewGuid();
        foreach (var line in kept)
        {
            if (line.Quantity > 1_000_000_000m)
            {
                return ServiceResult<DeliveryView>.Bad("Количество от нуля до миллиарда.");
            }

            var product = await db.Products.FirstOrDefaultAsync(row => row.Id == line.ProductId && row.IsActive, cancellationToken);
            if (product is null)
            {
                return ServiceResult<DeliveryView>.Bad("Продукта нет в каталоге.");
            }

            var expiresAt = line.ExpiresAt.TimeOfDay == TimeSpan.Zero ? EndOfKitchenDay(line.ExpiresAt) : SpecifyUtc(line.ExpiresAt);
            if (expiresAt <= now || expiresAt.Year >= 2090)
            {
                return ServiceResult<DeliveryView>.Bad($"У «{product.Name}» укажите срок годности позже сегодня.");
            }

            batches.Add(new Batch
            {
                Id = Guid.NewGuid(),
                DeliveryId = id,
                ProductId = product.Id,
                QuantityInitial = line.Quantity,
                QuantityRemaining = line.Quantity,
                UnitCost = product.PurchasePrice,
                ReceivedAt = now,
                ExpiresAt = expiresAt,
                Status = BatchStatus.Arrived,
            });
        }

        db.Deliveries.Add(new Delivery
        {
            Id = id,
            Number = $"СТД-{now:yyyyMMdd-HHmmss}",
            Supplier = "Стандартная закупка",
            CreatedAt = now,
            Stage = DeliveryStage.Parked,
            Batches = batches,
        });
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<DeliveryView>.Ok((await GetAsync(id, cancellationToken))!);
    }

    /// <summary>Менеджер правит предложение, пока оно ещё не в приёмке. Ноль убирает строку. Склад не меняется.</summary>
    public async Task<ServiceResult<DeliveryView>> ReviseParkedAsync(Guid id, IReadOnlyList<(Guid BatchId, decimal Quantity, DateTime? ExpiresAt)> lines, CancellationToken cancellationToken)
    {
        var delivery = await db.Deliveries.Include(row => row.Batches).FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (delivery is null)
        {
            return ServiceResult<DeliveryView>.Bad("Поставка не найдена.");
        }

        if (delivery.Stage != DeliveryStage.Parked)
        {
            return ServiceResult<DeliveryView>.Bad("Править можно только предложение, которое ещё не в приёмке.");
        }

        var byId = lines.ToDictionary(line => line.BatchId);
        var now = clock.UtcNow;
        foreach (var batch in delivery.Batches.Where(batch => batch.Status == BatchStatus.Arrived).ToList())
        {
            if (!byId.TryGetValue(batch.Id, out var line) || line.Quantity <= 0)
            {
                db.Batches.Remove(batch);
                continue;
            }

            if (line.Quantity > 1_000_000_000m)
            {
                return ServiceResult<DeliveryView>.Bad("Количество от нуля до миллиарда.");
            }

            batch.QuantityInitial = line.Quantity;
            batch.QuantityRemaining = line.Quantity;
            if (line.ExpiresAt is DateTime dated)
            {
                var expiresAt = dated.TimeOfDay == TimeSpan.Zero ? EndOfKitchenDay(dated) : SpecifyUtc(dated);
                if (expiresAt <= now || expiresAt.Year >= 2090)
                {
                    return ServiceResult<DeliveryView>.Bad("Срок годности должен быть позже сегодня.");
                }

                batch.ExpiresAt = expiresAt;
            }
        }

        if (!delivery.Batches.Any(batch => batch.Status == BatchStatus.Arrived && db.Entry(batch).State != EntityState.Deleted))
        {
            return ServiceResult<DeliveryView>.Bad("Оставьте хотя бы одну позицию.");
        }

        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<DeliveryView>.Ok((await GetAsync(id, cancellationToken))!);
    }

    private static decimal StandingQuantity(string name, Unit unit)
    {
        var key = name.ToLowerInvariant();
        if (key.Contains("лосос")) return 4000;
        if (key.Contains("моцарел")) return 3000;
        if (key.Contains("томат")) return 5000;
        if (key.Contains("базилик")) return 200;
        if (key.Contains("спагет") || key.Contains("паста")) return 5000;
        if (key.Contains("сливк")) return 3000;
        if (key.Contains("кофе")) return 2000;
        if (key.Contains("молок")) return 10000;
        if (key.Contains("куриц")) return 6000;
        if (key.Contains("говяд")) return 5000;
        if (key.Contains("лук")) return 3000;
        if (key.Contains("картоф")) return 8000;
        if (key.Contains("мука")) return 5000;
        if (key.Contains("яйц")) return 90;
        if (key.Contains("сливочн")) return 2000;
        if (key.Contains("масло")) return 3000;
        if (key.Contains("сахар")) return 2000;
        if (key.Contains("кревет")) return 2000;
        if (key.Contains("лимон")) return 1500;
        if (key.Contains("чеснок")) return 500;
        if (key.Contains("рис")) return 4000;
        if (key.Contains("огурец")) return 3000;
        if (key.Contains("салат")) return 2000;
        return unit switch
        {
            Unit.Pcs => 30,
            Unit.Ml => 2000,
            _ => 2000,
        };
    }

    /// <summary>Примерный срок с этикетки, в днях от сегодня. Кухня на приёмке ставит дату с пачки.</summary>
    private static int StandingDays(string name)
    {
        var key = name.ToLowerInvariant();
        if (key.Contains("лосос") || key.Contains("кревет")) return 2;
        if (key.Contains("куриц") || key.Contains("салат")) return 4;
        if (key.Contains("говяд") || key.Contains("молок") || key.Contains("базилик")) return 5;
        if (key.Contains("томат") || key.Contains("огурец")) return 6;
        if (key.Contains("сливк")) return 7;
        if (key.Contains("лимон")) return 10;
        if (key.Contains("моцарел") || key.Contains("лук")) return 12;
        if (key.Contains("картоф")) return 18;
        if (key.Contains("чеснок")) return 20;
        if (key.Contains("яйц") || key.Contains("сливочн")) return 21;
        if (key.Contains("кофе") || key.Contains("мука") || key.Contains("сахар") || key.Contains("рис") || key.Contains("спагет") || key.Contains("масло")) return 90;
        return 7;
    }

    public async Task<IReadOnlyList<SupplyProductView>> CatalogAsync(CancellationToken cancellationToken) =>
        await db.Products.AsNoTracking()
            .Where(product => product.IsActive)
            .OrderBy(product => product.Name)
            .Select(product => new SupplyProductView(product.Id, product.Name, product.Unit))
            .ToListAsync(cancellationToken);

    /// <summary>Поставщик привёз позиции сверх заказа. Тоже «приехала», склад и открытый заказ не трогает.</summary>
    public async Task<ServiceResult<DeliveryView>> SendExtraAsync(IReadOnlyList<SupplyExtraLine> lines, CancellationToken cancellationToken)
    {
        if (lines.Count == 0)
        {
            return ServiceResult<DeliveryView>.Bad("Нечего привозить.");
        }

        var now = clock.UtcNow;
        var batches = new List<Batch>();
        var id = Guid.NewGuid();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0 || line.Quantity > 1_000_000_000m)
            {
                return ServiceResult<DeliveryView>.Bad("Количество от нуля до миллиарда.");
            }

            var product = await db.Products.FirstOrDefaultAsync(row => row.Id == line.ProductId && row.IsActive, cancellationToken);
            if (product is null)
            {
                return ServiceResult<DeliveryView>.Bad("Продукта нет в каталоге.");
            }

            var expiresAt = line.ExpiresAt.TimeOfDay == TimeSpan.Zero ? EndOfKitchenDay(line.ExpiresAt) : SpecifyUtc(line.ExpiresAt);
            if (expiresAt <= now)
            {
                return ServiceResult<DeliveryView>.Bad("Срок годности должен быть позже сегодня.");
            }

            batches.Add(new Batch
            {
                Id = Guid.NewGuid(),
                DeliveryId = id,
                ProductId = product.Id,
                QuantityInitial = line.Quantity,
                QuantityRemaining = line.Quantity,
                UnitCost = product.PurchasePrice,
                ReceivedAt = now,
                ExpiresAt = expiresAt,
                Status = BatchStatus.Arrived,
            });
        }

        db.Deliveries.Add(new Delivery
        {
            Id = id,
            Number = $"ДОП-{now:yyyyMMdd-HHmmss}",
            Supplier = "Довезли сами",
            CreatedAt = now,
            Stage = DeliveryStage.Parked,
            Batches = batches,
        });
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<DeliveryView>.Ok((await GetAsync(id, cancellationToken))!);
    }

    /// <summary>Менеджер отдаёт «приехала» кухне. Остаток не меняется.</summary>
    public async Task<ServiceResult<DeliveryView>> StartReceivingAsync(Guid id, CancellationToken cancellationToken)
    {
        var delivery = await db.Deliveries.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (delivery is null)
        {
            return ServiceResult<DeliveryView>.Bad("Поставка не найдена.");
        }

        if (delivery.Stage == DeliveryStage.Receiving)
        {
            return ServiceResult<DeliveryView>.Ok((await GetAsync(id, cancellationToken))!);
        }

        if (delivery.Stage != DeliveryStage.Parked)
        {
            return ServiceResult<DeliveryView>.Bad("В приёмку уходит только поставка, которая ещё ждёт.");
        }

        delivery.Stage = DeliveryStage.Receiving;
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<DeliveryView>.Ok((await GetAsync(id, cancellationToken))!);
    }

    public async Task<IReadOnlyList<DeliveryView>> IntakeAsync(CancellationToken cancellationToken)
    {
        var ids = await db.Deliveries.AsNoTracking()
            .Where(delivery => delivery.Stage == DeliveryStage.Receiving || delivery.Stage == DeliveryStage.KitchenDone)
            .OrderBy(delivery => delivery.CreatedAt)
            .Select(delivery => delivery.Id)
            .ToListAsync(cancellationToken);
        var result = new List<DeliveryView>();
        foreach (var id in ids)
        {
            var view = await GetAsync(id, cancellationToken);
            if (view is not null)
            {
                result.Add(view);
            }
        }

        return result;
    }

    /// <summary>Кухня пишет, сколько взяла по каждой строке. Ноль — не взяли. Склад ещё не меняется.</summary>
    public async Task<ServiceResult<DeliveryView>> KitchenDecideAsync(Guid id, IReadOnlyList<IntakeLineCommand> lines, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockDeliveryAsync(id, cancellationToken))
        {
            return ServiceResult<DeliveryView>.Bad("Поставка не найдена.");
        }

        var delivery = await db.Deliveries.Include(row => row.Batches).FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (delivery is null)
        {
            return ServiceResult<DeliveryView>.Bad("Поставка не найдена.");
        }

        if (delivery.AcceptedAt is not null || delivery.Stage == DeliveryStage.Accepted)
        {
            return ServiceResult<DeliveryView>.Ok((await GetAsync(id, cancellationToken))!);
        }

        if (delivery.Stage is not (DeliveryStage.Receiving or DeliveryStage.KitchenDone))
        {
            return ServiceResult<DeliveryView>.Bad("Сначала менеджер отдаёт поставку в приёмку.");
        }

        if (lines.Count == 0)
        {
            return ServiceResult<DeliveryView>.Bad("Отметьте каждую строку.");
        }

        var waiting = delivery.Batches.Where(batch => batch.Status == BatchStatus.Arrived).ToList();
        if (lines.Select(line => line.BatchId).Distinct().Count() != waiting.Count || lines.Count != waiting.Count)
        {
            return ServiceResult<DeliveryView>.Bad("Отметьте каждую строку.");
        }

        foreach (var line in lines)
        {
            var batch = waiting.FirstOrDefault(item => item.Id == line.BatchId);
            if (batch is null)
            {
                return ServiceResult<DeliveryView>.Bad("Строки нет в этой поставке.");
            }

            if (line.Quantity < 0 || line.Quantity > batch.QuantityInitial)
            {
                return ServiceResult<DeliveryView>.Bad("Количество от нуля до заявленного.");
            }

            if (line.Quantity > 0 && line.ExpiresAt is null)
            {
                return ServiceResult<DeliveryView>.Bad("Укажите срок с пачки.");
            }

            if (line.ExpiresAt is DateTime dated)
            {
                var expiresAt = dated.TimeOfDay == TimeSpan.Zero ? EndOfKitchenDay(dated) : SpecifyUtc(dated);
                if (line.Quantity > 0 && (expiresAt.Year >= 2090 || expiresAt <= batch.ReceivedAt))
                {
                    return ServiceResult<DeliveryView>.Bad("Укажите срок с пачки.");
                }

                if (expiresAt > batch.ReceivedAt && expiresAt.Year < 2090)
                {
                    batch.ExpiresAt = expiresAt;
                }
            }

            if (line.Quantity == 0)
            {
                var note = line.Note?.Trim();
                if (string.IsNullOrEmpty(note))
                {
                    return ServiceResult<DeliveryView>.Bad("Для строки, которую не берём, укажите почему.");
                }

                batch.KitchenNote = note.Length > 300 ? note[..300] : note;
            }
            else
            {
                batch.KitchenNote = null;
            }

            batch.AcceptedQuantity = line.Quantity;
        }

        delivery.Stage = DeliveryStage.KitchenDone;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ServiceResult<DeliveryView>.Ok((await GetAsync(id, cancellationToken))!);
    }

    /// <summary>Итог: на склад попадает только взятое. Ноль не приходуется. Повтор не удваивает остаток.</summary>
    public async Task<ServiceResult<DeliveryView>> AcceptAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockDeliveryAsync(id, cancellationToken))
        {
            return ServiceResult<DeliveryView>.Bad("Поставка не найдена.");
        }

        var delivery = await db.Deliveries.Include(row => row.Batches).FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (delivery is null)
        {
            return ServiceResult<DeliveryView>.Bad("Поставка не найдена.");
        }

        if (delivery.AcceptedAt is not null)
        {
            return ServiceResult<DeliveryView>.Ok((await GetAsync(id, cancellationToken))!);
        }

        if (delivery.Stage != DeliveryStage.KitchenDone)
        {
            return ServiceResult<DeliveryView>.Bad("Сначала кухня отмечает, сколько взяли.");
        }

        if (delivery.Batches.Any(batch => batch.Status == BatchStatus.Arrived && batch.AcceptedQuantity is null))
        {
            return ServiceResult<DeliveryView>.Bad("Кухня ещё не отметила эту строку.");
        }

        if (delivery.Batches.Any(batch => batch.Status == BatchStatus.Arrived && (batch.AcceptedQuantity ?? 0) > 0 && batch.ExpiresAt.Year >= 2090))
        {
            return ServiceResult<DeliveryView>.Bad("Укажите срок с пачки.");
        }

        var now = clock.UtcNow;
        var takenProducts = new List<Guid>();
        foreach (var batch in delivery.Batches.Where(batch => batch.Status == BatchStatus.Arrived))
        {
            if (batch.AcceptedQuantity is null)
            {
                return ServiceResult<DeliveryView>.Bad("Кухня ещё не отметила эту строку.");
            }

            var taken = batch.AcceptedQuantity.Value;
            if (taken > 0)
            {
                batch.QuantityRemaining = taken;
                batch.Status = BatchStatus.Active;
                db.StockMovements.Add(new StockMovement
                {
                    Id = Guid.NewGuid(),
                    BatchId = batch.Id,
                    Type = MovementType.Receipt,
                    Quantity = taken,
                    CreatedAt = now,
                });
                takenProducts.Add(batch.ProductId);
            }
            else
            {
                batch.QuantityRemaining = 0;
                batch.Status = BatchStatus.Depleted;
            }
        }

        delivery.AcceptedAt = now;
        delivery.ReceivedBy = userId;
        delivery.Stage = DeliveryStage.Accepted;
        var rejectedProducts = delivery.Batches
            .GroupBy(batch => batch.ProductId)
            .Where(group => group.All(batch => (batch.AcceptedQuantity ?? 0) == 0))
            .Select(group => group.Key)
            .ToList();
        if (rejectedProducts.Count > 0)
        {
            var back = await db.PurchaseLines
                .Where(line => line.FulfilledByDeliveryId == id && rejectedProducts.Contains(line.ProductId))
                .ToListAsync(cancellationToken);
            foreach (var line in back)
            {
                line.FulfilledAt = null;
                line.FulfilledByDeliveryId = null;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (takenProducts.Count > 0)
        {
            await broadcaster.PublishAsync(new StockChange(takenProducts.Distinct().ToList(), [], [], null, null, null), cancellationToken);
        }

        return ServiceResult<DeliveryView>.Ok((await GetAsync(id, cancellationToken))!);
    }

    /// <summary>Вся поставка снимается, пока она не на складе. Строки заказа снова открыты. Повтор ничего не меняет.</summary>
    public async Task<ServiceResult<DeliveryView>> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockDeliveryAsync(id, cancellationToken))
        {
            return ServiceResult<DeliveryView>.Bad("Поставка не найдена.");
        }

        var delivery = await db.Deliveries.Include(row => row.Batches).FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (delivery is null)
        {
            return ServiceResult<DeliveryView>.Bad("Поставка не найдена.");
        }

        if (delivery.Stage == DeliveryStage.Cancelled)
        {
            await transaction.CommitAsync(cancellationToken);
            return ServiceResult<DeliveryView>.Ok((await GetAsync(id, cancellationToken))!);
        }

        if (delivery.AcceptedAt is not null || delivery.Stage == DeliveryStage.Accepted)
        {
            return ServiceResult<DeliveryView>.Bad("Поставка уже на складе. Её целиком не снять.");
        }

        if (delivery.Stage is not (DeliveryStage.Parked or DeliveryStage.Receiving or DeliveryStage.KitchenDone))
        {
            return ServiceResult<DeliveryView>.Bad("Эту поставку уже нельзя отменить.");
        }

        foreach (var batch in delivery.Batches)
        {
            batch.QuantityRemaining = 0;
            batch.AcceptedQuantity = 0;
            batch.Status = BatchStatus.Depleted;
            batch.KitchenNote = "Поставку отменили целиком";
        }

        var lines = await db.PurchaseLines
            .Where(line => line.FulfilledByDeliveryId == id)
            .ToListAsync(cancellationToken);
        foreach (var line in lines)
        {
            line.FulfilledAt = null;
            line.FulfilledByDeliveryId = null;
        }

        delivery.Stage = DeliveryStage.Cancelled;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ServiceResult<DeliveryView>.Ok((await GetAsync(id, cancellationToken))!);
    }

    private async Task LockOpenPurchaseLinesAsync(CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = "SELECT id FROM purchase_lines WHERE fulfilled_at IS NULL FOR UPDATE";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
        }
    }

    private async Task<bool> LockDeliveryAsync(Guid id, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = "SELECT id FROM deliveries WHERE id = @id FOR UPDATE";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = id;
        command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken);
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
            batch.CertificateExpiresAt,
            batch.AcceptedQuantity,
            batch.KitchenNote);
    }

    /// <summary>Дата без времени — до конца этого календарного дня по кухне, UTC+3.</summary>
    private static DateTime EndOfKitchenDay(DateTime value)
    {
        var day = DateOnly.FromDateTime(value);
        return DateTime.SpecifyKind(day.ToDateTime(new TimeOnly(21, 0)), DateTimeKind.Utc);
    }

    private static DateTime SpecifyUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
