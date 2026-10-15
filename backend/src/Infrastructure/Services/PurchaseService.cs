using Microsoft.EntityFrameworkCore;
using Monitoring.Core;
using Monitoring.Core.Time;
using Monitoring.Infrastructure.Persistence;

namespace Monitoring.Infrastructure.Services;

/// <summary>Заказ поставщику: список до машины. Приход его закрывает, сам склад не трогает.</summary>
public sealed class PurchaseService(AppDbContext db, IClock clock, StockReadModel stock)
{
    public async Task<IReadOnlyList<PurchaseLineView>> ListAsync(bool openOnly, CancellationToken cancellationToken)
    {
        var query = db.PurchaseLines.AsNoTracking()
            .Include(line => line.Product)
            .Include(line => line.RequestedByUser)
            .AsQueryable();
        if (openOnly)
        {
            query = query.Where(line => line.FulfilledAt == null);
        }

        var rows = await query
            .OrderBy(line => line.FulfilledAt != null)
            .ThenBy(line => line.CreatedAt)
            .ToListAsync(cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task<ServiceResult<PurchaseLineView>> AddAsync(AddPurchaseLineCommand command, CancellationToken cancellationToken)
    {
        if (command.Quantity <= 0)
        {
            return ServiceResult<PurchaseLineView>.Bad("Количество должно быть больше нуля.");
        }

        if (command.Quantity > MaxQuantity)
        {
            return ServiceResult<PurchaseLineView>.Bad("Количество слишком большое.");
        }

        var product = await db.Products.FirstOrDefaultAsync(row => row.Id == command.ProductId && row.IsActive, cancellationToken);
        if (product is null)
        {
            return ServiceResult<PurchaseLineView>.Bad("Сначала заведите продукт в каталоге.");
        }

        var note = string.IsNullOrWhiteSpace(command.Note) ? null : command.Note.Trim();
        var open = await db.PurchaseLines.FirstOrDefaultAsync(
            line => line.ProductId == command.ProductId && line.FulfilledAt == null && line.Source == command.Source,
            cancellationToken);
        if (open is not null)
        {
            if (open.Quantity + command.Quantity > MaxQuantity)
            {
                return ServiceResult<PurchaseLineView>.Bad("Количество слишком большое.");
            }

            open.Quantity += command.Quantity;
            if (note is not null)
            {
                open.Note = string.IsNullOrWhiteSpace(open.Note) ? note : $"{open.Note}; {note}";
            }

            await db.SaveChangesAsync(cancellationToken);
            return ServiceResult<PurchaseLineView>.Ok(await GetAsync(open.Id, cancellationToken));
        }

        var line = new PurchaseLine
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Quantity = command.Quantity,
            Note = note,
            Source = command.Source,
            RequestedBy = command.RequestedBy,
            CreatedAt = clock.UtcNow,
        };
        db.PurchaseLines.Add(line);
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<PurchaseLineView>.Ok(await GetAsync(line.Id, cancellationToken));
    }

    public async Task<ServiceResult<int>> AddForecastAsync(IReadOnlyList<Guid>? productIds, Guid requestedBy, CancellationToken cancellationToken)
    {
        var report = await stock.WeeklyForecastAsync(cancellationToken);
        var selected = report.Items.Where(item => item.Shortfall > 0).ToList();
        if (productIds is { Count: > 0 })
        {
            var wanted = productIds.ToHashSet();
            selected = selected.Where(item => wanted.Contains(item.ProductId)).ToList();
        }

        if (selected.Count == 0)
        {
            return ServiceResult<int>.Bad("Нечего заказывать: по прогнозу недели дефицита нет.");
        }

        var added = 0;
        foreach (var item in selected)
        {
            var result = await AddAsync(
                new AddPurchaseLineCommand(item.ProductId, item.Shortfall, "прогноз на неделю", PurchaseSource.Forecast, requestedBy),
                cancellationToken);
            if (result.Error is null)
            {
                added++;
            }
        }

        return added == 0
            ? ServiceResult<int>.Bad("Строки прогноза не добавились.")
            : ServiceResult<int>.Ok(added);
    }

    public async Task<ServiceResult<bool>> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        var line = await db.PurchaseLines.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (line is null)
        {
            return ServiceResult<bool>.Bad("Строки заказа нет.");
        }

        if (line.FulfilledAt is not null)
        {
            return ServiceResult<bool>.Bad("Эту строку уже приняли.");
        }

        db.PurchaseLines.Remove(line);
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<bool>.Ok(true);
    }

    public async Task FulfillForDeliveryAsync(Guid deliveryId, IReadOnlyCollection<Guid> productIds, DateTime orderedBefore, CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
        {
            return;
        }

        var open = await db.PurchaseLines
            .Where(line => line.FulfilledAt == null && line.CreatedAt <= orderedBefore && productIds.Contains(line.ProductId))
            .ToListAsync(cancellationToken);
        if (open.Count == 0)
        {
            return;
        }

        var now = clock.UtcNow;
        foreach (var line in open)
        {
            line.FulfilledAt = now;
            line.FulfilledByDeliveryId = deliveryId;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<PurchaseLineView> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var line = await db.PurchaseLines.AsNoTracking()
            .Include(row => row.Product)
            .Include(row => row.RequestedByUser)
            .SingleAsync(row => row.Id == id, cancellationToken);
        return Map(line);
    }

    private const decimal MaxQuantity = 1_000_000_000m;

    private static PurchaseLineView Map(PurchaseLine line) => new(
        line.Id,
        line.ProductId,
        line.Product.Name,
        line.Product.Unit,
        line.Quantity,
        line.Note,
        line.Source,
        line.RequestedByUser.Login,
        line.CreatedAt,
        line.FulfilledAt);
}
