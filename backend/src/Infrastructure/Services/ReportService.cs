using Microsoft.EntityFrameworkCore;
using Monitoring.Core;
using Monitoring.Infrastructure.Persistence;

namespace Monitoring.Infrastructure.Services;

/// <summary>
/// Деньги смены. Закупка — количество прихода умножить на цену партии.
/// Выручка — принятые порции по цене меню. Себестоимость — списание в заказ по цене той партии.
/// Чистыми = выручка минус себестоимость проданного. Брак и срок — отдельно, это не продажа.
/// </summary>
public sealed class ReportService(AppDbContext db)
{
    public async Task<MoneyReport> BuildAsync(CancellationToken cancellationToken)
    {
        var batches = (await db.Batches.AsNoTracking()
            .Include(batch => batch.Product)
            .Include(batch => batch.Delivery)
            .ToListAsync(cancellationToken))
            .Where(batch => PurchasedQuantity(batch) > 0)
            .ToList();
        var users = await db.Users.AsNoTracking().OrderBy(user => user.DisplayName).ThenBy(user => user.Login).ToListAsync(cancellationToken);
        var orders = await db.Orders.AsNoTracking()
            .Include(order => order.Items).ThenInclude(item => item.Dish).ThenInclude(dish => dish.Ingredients)
            .Where(order => order.Status == OrderStatus.Accepted)
            .ToListAsync(cancellationToken);
        var movements = await db.StockMovements.AsNoTracking()
            .Include(movement => movement.Batch)
            .Where(movement => movement.Type != MovementType.Receipt)
            .ToListAsync(cancellationToken);

        var purchaseRub = batches.Sum(batch => PurchasedQuantity(batch) * batch.UnitCost);
        var purchases = batches
            .GroupBy(batch => batch.ProductId)
            .Select(group =>
            {
                var product = group.First().Product;
                return new PurchaseMoneyRow(product.Id, product.Name, product.Unit, group.Sum(PurchasedQuantity), group.Sum(batch => PurchasedQuantity(batch) * batch.UnitCost));
            })
            .OrderByDescending(row => row.Rub)
            .ToList();

        var sold = orders.SelectMany(order => order.Items.Select(item => new { order.CreatedBy, item })).ToList();
        var revenue = sold.Sum(line => line.item.Portions * line.item.Dish.SalePrice);
        var cost = movements.Where(movement => movement.Type == MovementType.OrderWriteOff).Sum(movement => movement.Quantity * movement.Batch.UnitCost);
        var waste = movements.Where(movement => movement.Type is MovementType.ExpiryWriteOff or MovementType.KitchenWriteOff)
            .Sum(movement => movement.Quantity * movement.Batch.UnitCost);

        var claims = await db.PurchaseLines.AsNoTracking()
            .Where(line => line.FulfilledAt != null && line.FulfilledByDeliveryId != null)
            .Select(line => new PurchaseClaim(line.FulfilledByDeliveryId!.Value, line.ProductId, line.RequestedBy, line.Quantity))
            .ToListAsync(cancellationToken);
        var staff = users.Select(user =>
        {
            var own = batches.Where(batch => BuyerId(batch, claims) == user.Id).ToList();
            var ownOrders = orders.Where(order => order.CreatedBy == user.Id).ToList();
            return new StaffMoneyRow(
                string.IsNullOrWhiteSpace(user.DisplayName) ? user.Login : user.DisplayName,
                user.Role.ToString(),
                own.Sum(batch => PurchasedQuantity(batch) * batch.UnitCost),
                own.Select(batch => batch.DeliveryId).Distinct().Count(),
                ownOrders.SelectMany(order => order.Items).Sum(item => item.Portions * item.Dish.SalePrice),
                ownOrders.Count);
        }).Where(row => row.PurchaseRub > 0 || row.Orders > 0 || row.SalesRub > 0)
            .OrderByDescending(row => row.PurchaseRub)
            .ThenByDescending(row => row.SalesRub)
            .ToList();

        var dishes = sold
            .GroupBy(line => line.item.DishId)
            .Select(group =>
            {
                var dish = group.First().item.Dish;
                var portions = group.Sum(line => line.item.Portions);
                var dishRevenue = portions * dish.SalePrice;
                var dishCost = dish.Ingredients.Count == 0
                    ? 0
                    : portions * RecipeCost(dish, batches);
                return new DishMoneyRow(dish.Name, dish.SalePrice, portions, dishRevenue, dishCost, dishRevenue - dishCost);
            })
            .OrderByDescending(row => row.RevenueRub)
            .ToList();

        return new MoneyReport(decimal.Round(purchaseRub, 2), decimal.Round(revenue, 2), decimal.Round(cost, 2), decimal.Round(waste, 2), decimal.Round(revenue - cost, 2), staff, purchases, dishes);
    }

    /// <summary>
    /// Закупки за последние days суток, считая сегодняшний день. days пустой или 0 — за всё время.
    /// День берётся из даты поставки. Списания — срок и акты кухни, не продажа в заказ.
    /// </summary>
    public async Task<SpendReport> SpendAsync(int? days, CancellationToken cancellationToken)
    {
        var from = days is > 0
            ? DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(1 - days.Value), DateTimeKind.Utc)
            : (DateTime?)null;
        var batches = (await db.Batches.AsNoTracking()
            .Include(batch => batch.Product)
            .Include(batch => batch.Delivery)
            .ToListAsync(cancellationToken))
            .Where(batch => PurchasedQuantity(batch) > 0)
            .ToList();
        if (from is not null)
        {
            batches = batches.Where(batch => batch.Delivery.CreatedAt >= from).ToList();
        }

        var users = await db.Users.AsNoTracking().ToDictionaryAsync(user => user.Id, cancellationToken);
        var spentByDay = batches
            .GroupBy(batch => DateOnly.FromDateTime(batch.Delivery.CreatedAt))
            .ToDictionary(
                group => group.Key,
                group => new PurchaseDayRow(
                    group.Key,
                    decimal.Round(group.Sum(batch => PurchasedQuantity(batch) * batch.UnitCost), 2),
                    group.Select(batch => batch.DeliveryId).Distinct().Count()));
        var endDay = DateOnly.FromDateTime(DateTime.UtcNow);
        var startDay = from is null
            ? (spentByDay.Count == 0 ? endDay.AddDays(-29) : spentByDay.Keys.Min())
            : DateOnly.FromDateTime(from.Value);
        if (startDay < endDay.AddDays(-89))
        {
            startDay = endDay.AddDays(-89);
        }

        var dayRows = new List<PurchaseDayRow>();
        for (var day = startDay; day <= endDay; day = day.AddDays(1))
        {
            dayRows.Add(spentByDay.TryGetValue(day, out var row) ? row : new PurchaseDayRow(day, 0, 0));
        }
        var claims = await db.PurchaseLines.AsNoTracking()
            .Where(line => line.FulfilledAt != null && line.FulfilledByDeliveryId != null)
            .Select(line => new PurchaseClaim(line.FulfilledByDeliveryId!.Value, line.ProductId, line.RequestedBy, line.Quantity))
            .ToListAsync(cancellationToken);
        var people = batches
            .GroupBy(batch => BuyerId(batch, claims))
            .Select(group =>
            {
                var user = group.Key is Guid id && users.TryGetValue(id, out var found) ? found : null;
                var name = user is null || string.IsNullOrWhiteSpace(user.DisplayName) ? user?.Login ?? "Без сотрудника" : user.DisplayName;
                return new SpendPersonRow(
                    name,
                    user?.Role.ToString() ?? "",
                    decimal.Round(group.Sum(batch => PurchasedQuantity(batch) * batch.UnitCost), 2),
                    group.Select(batch => batch.DeliveryId).Distinct().Count());
            })
            .Where(row => row.Rub > 0)
            .OrderByDescending(row => row.Rub)
            .ToList();
        var products = batches
            .GroupBy(batch => batch.ProductId)
            .Select(group =>
            {
                var product = group.First().Product;
                return new PurchaseMoneyRow(product.Id, product.Name, product.Unit, group.Sum(PurchasedQuantity), decimal.Round(group.Sum(batch => PurchasedQuantity(batch) * batch.UnitCost), 2));
            })
            .Where(row => row.Rub > 0)
            .OrderByDescending(row => row.Rub)
            .ToList();

        var movements = await db.StockMovements.AsNoTracking()
            .Include(movement => movement.Batch)
            .Where(movement => movement.Type == MovementType.ExpiryWriteOff || movement.Type == MovementType.KitchenWriteOff)
            .ToListAsync(cancellationToken);
        if (from is not null)
        {
            movements = movements.Where(movement => movement.CreatedAt >= from).ToList();
        }

        var waste = movements
            .GroupBy(movement => movement.Type == MovementType.ExpiryWriteOff
                ? nameof(WriteOffReason.Expiry)
                : string.IsNullOrWhiteSpace(movement.Reason) ? nameof(WriteOffReason.Waste) : movement.Reason)
            .Select(group => new WasteMoneyRow(group.Key, decimal.Round(group.Sum(movement => movement.Quantity * movement.Batch.UnitCost), 2)))
            .Where(row => row.Rub > 0)
            .OrderByDescending(row => row.Rub)
            .ToList();

        return new SpendReport(decimal.Round(batches.Sum(batch => PurchasedQuantity(batch) * batch.UnitCost), 2), dayRows, people, products, waste);
    }

    private sealed record PurchaseClaim(Guid DeliveryId, Guid ProductId, Guid RequestedBy, decimal Quantity);

    private static Guid? BuyerId(Batch batch, IReadOnlyList<PurchaseClaim> claims)
    {
        var match = claims
            .Where(line => line.DeliveryId == batch.DeliveryId && line.ProductId == batch.ProductId)
            .OrderByDescending(line => line.Quantity)
            .FirstOrDefault();
        return match?.RequestedBy ?? batch.Delivery.ReceivedBy;
    }

    /// <summary>В закупку входит только то, что стало складом. Заявленное и отказ не считаются.</summary>
    private static decimal PurchasedQuantity(Batch batch)
    {
        if (batch.Status == BatchStatus.Arrived || batch.AcceptedQuantity == 0)
        {
            return 0;
        }

        return batch.AcceptedQuantity ?? batch.QuantityInitial;
    }

    private static decimal RecipeCost(Dish dish, IReadOnlyList<Batch> batches)
    {
        decimal sum = 0;
        foreach (var line in dish.Ingredients)
        {
            var price = batches.Where(batch => batch.ProductId == line.ProductId && batch.UnitCost > 0)
                .Select(batch => batch.UnitCost)
                .DefaultIfEmpty(0)
                .Average();
            sum += line.Quantity * price;
        }

        return sum;
    }
}
