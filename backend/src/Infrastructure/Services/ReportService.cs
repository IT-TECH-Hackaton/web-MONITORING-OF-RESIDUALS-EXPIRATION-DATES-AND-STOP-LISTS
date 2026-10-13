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
        var batches = await db.Batches.AsNoTracking()
            .Include(batch => batch.Product)
            .Include(batch => batch.Delivery)
            .ToListAsync(cancellationToken);
        var users = await db.Users.AsNoTracking().OrderBy(user => user.DisplayName).ThenBy(user => user.Login).ToListAsync(cancellationToken);
        var orders = await db.Orders.AsNoTracking()
            .Include(order => order.Items).ThenInclude(item => item.Dish).ThenInclude(dish => dish.Ingredients)
            .Where(order => order.Status == OrderStatus.Accepted)
            .ToListAsync(cancellationToken);
        var movements = await db.StockMovements.AsNoTracking()
            .Include(movement => movement.Batch)
            .Where(movement => movement.Type != MovementType.Receipt)
            .ToListAsync(cancellationToken);

        var purchaseRub = batches.Sum(batch => batch.QuantityInitial * batch.UnitCost);
        var purchases = batches
            .GroupBy(batch => batch.ProductId)
            .Select(group =>
            {
                var product = group.First().Product;
                return new PurchaseMoneyRow(product.Id, product.Name, product.Unit, group.Sum(batch => batch.QuantityInitial), group.Sum(batch => batch.QuantityInitial * batch.UnitCost));
            })
            .OrderByDescending(row => row.Rub)
            .ToList();

        var sold = orders.SelectMany(order => order.Items.Select(item => new { order.CreatedBy, item })).ToList();
        var revenue = sold.Sum(line => line.item.Portions * line.item.Dish.SalePrice);
        var cost = movements.Where(movement => movement.Type == MovementType.OrderWriteOff).Sum(movement => movement.Quantity * movement.Batch.UnitCost);
        var waste = movements.Where(movement => movement.Type is MovementType.ExpiryWriteOff or MovementType.KitchenWriteOff)
            .Sum(movement => movement.Quantity * movement.Batch.UnitCost);

        var staff = users.Select(user =>
        {
            var own = batches.Where(batch => batch.Delivery.ReceivedBy == user.Id).ToList();
            var ownOrders = orders.Where(order => order.CreatedBy == user.Id).ToList();
            return new StaffMoneyRow(
                string.IsNullOrWhiteSpace(user.DisplayName) ? user.Login : user.DisplayName,
                user.Role.ToString(),
                own.Sum(batch => batch.QuantityInitial * batch.UnitCost),
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
