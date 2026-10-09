using Microsoft.EntityFrameworkCore;
using Monitoring.Core;
using Monitoring.Core.Inventory;
using Monitoring.Core.Time;
using Monitoring.Infrastructure.Persistence;

namespace Monitoring.Infrastructure.Services;

public sealed class StockReadModel(AppDbContext db, IClock clock)
{
    public async Task<IReadOnlyList<StockItemView>> StockAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var products = await db.Products.AsNoTracking().Where(product => product.IsActive).OrderBy(product => product.Name).ToListAsync(cancellationToken);
        var batches = await db.Batches.AsNoTracking().ToListAsync(cancellationToken);
        return products.Select(product =>
        {
            var own = batches.Where(batch => batch.ProductId == product.Id).ToList();
            var availableBatches = own.Where(batch => IsAvailable(batch, now)).OrderBy(batch => batch.ExpiresAt).ToList();
            var available = availableBatches.Sum(batch => batch.QuantityRemaining);
            var nearest = availableBatches.FirstOrDefault()?.ExpiresAt;
            var anyExpired = own.Any(batch => batch.Status == BatchStatus.Expired || batch.ExpiresAt <= now);
            return new StockItemView(
                product.Id,
                product.Name,
                product.Unit,
                available,
                nearest,
                nearest is null ? null : FreshnessRules.HoursLeft(nearest.Value, now),
                FreshnessRules.ForProduct(available, nearest, anyExpired, now));
        }).ToList();
    }

    public async Task<IReadOnlyList<BatchView>> BatchesAsync(Guid productId, CancellationToken cancellationToken)
    {
        var batches = await BatchQuery()
            .Where(batch => batch.ProductId == productId)
            .OrderBy(batch => batch.ExpiresAt)
            .ToListAsync(cancellationToken);
        return batches.Select(MapBatch).ToList();
    }

    public async Task<IReadOnlyList<StopListEntry>> StopListAsync(CancellationToken cancellationToken)
    {
        var recipes = await RecipesAsync(cancellationToken);
        var stock = await AvailabilityAsync(cancellationToken);
        return StopListCalculator.Calculate(recipes, stock);
    }

    public async Task<IReadOnlyList<HotOffer>> HotOffersAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var recipes = await RecipesAsync(cancellationToken);
        var batches = await db.Batches.AsNoTracking()
            .Include(batch => batch.Delivery)
            .Where(batch => batch.Status == BatchStatus.Active && batch.QuantityRemaining > 0 && batch.ExpiresAt > now)
            .ToListAsync(cancellationToken);
        var hotBatches = batches.Select(batch => new HotBatch(
            batch.Id,
            batch.Delivery.Number,
            batch.ProductId,
            batch.QuantityRemaining,
            batch.ExpiresAt,
            batch.Status)).ToList();
        var available = batches
            .GroupBy(batch => batch.ProductId)
            .ToDictionary(group => group.Key, group => group.Sum(batch => batch.QuantityRemaining));
        return HotOfferRanker.Rank(recipes, hotBatches, available, now);
    }

    public async Task<DashboardView> DashboardAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var soon = now.AddHours(FreshnessRules.WarningHours);
        var dayStart = now.Date;
        var expiring = await BatchQuery()
            .Where(batch => batch.Status == BatchStatus.Active
                && batch.QuantityRemaining > 0
                && batch.ExpiresAt > now
                && batch.ExpiresAt <= soon)
            .OrderBy(batch => batch.ExpiresAt)
            .ToListAsync(cancellationToken);

        var stop = await StopListAsync(cancellationToken);
        var writeOffs = await db.StockMovements.AsNoTracking()
            .Include(movement => movement.Batch)
            .ThenInclude(batch => batch.Product)
            .Where(movement => movement.Type == MovementType.ExpiryWriteOff && movement.CreatedAt >= dayStart)
            .ToListAsync(cancellationToken);
        var expiryTotals = writeOffs
            .GroupBy(movement => movement.Batch.Product)
            .Select(group => new ExpiryTotalView(group.Key.Name, group.Key.Unit, group.Sum(movement => movement.Quantity)))
            .OrderByDescending(row => row.Quantity)
            .ToList();
        var acceptedToday = await db.Orders.CountAsync(
            order => order.Status == OrderStatus.Accepted && order.CreatedAt >= dayStart,
            cancellationToken);

        return new DashboardView(
            expiring.Select(MapBatch).ToList(),
            stop.Count(entry => entry.IsStopped),
            expiryTotals,
            acceptedToday,
            await ForecastsAsync(stop, cancellationToken),
            await WeeklyForecastAsync(cancellationToken));
    }

    public async Task<WeeklyForecastReport> WeeklyForecastAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var weekStart = WeeklyForecast.WeekStart(now);
        var movements = await db.StockMovements.AsNoTracking()
            .Include(movement => movement.Batch)
            .Where(movement => movement.Type == MovementType.OrderWriteOff && movement.CreatedAt >= weekStart)
            .ToListAsync(cancellationToken);
        var consumed = movements
            .GroupBy(movement => movement.Batch.ProductId)
            .ToDictionary(group => group.Key, group => group.Sum(movement => movement.Quantity));
        var ordersThisWeek = movements
            .Where(movement => movement.OrderId is not null)
            .Select(movement => movement.OrderId!.Value)
            .Distinct()
            .Count();

        var batches = await db.Batches.AsNoTracking()
            .Where(batch => batch.Status == BatchStatus.Active && batch.QuantityRemaining > 0 && batch.ExpiresAt > now)
            .ToListAsync(cancellationToken);
        var batchesByProduct = batches.GroupBy(batch => batch.ProductId)
            .ToDictionary(group => group.Key, group => group.Select(batch => new ForecastBatch(batch.QuantityRemaining, batch.ExpiresAt)).ToList());

        var recipes = await RecipesAsync(cancellationToken);
        var dishesByProduct = new Dictionary<Guid, List<string>>();
        foreach (var recipe in recipes)
        {
            foreach (var line in recipe.Lines)
            {
                if (!dishesByProduct.TryGetValue(line.ProductId, out var names))
                {
                    names = [];
                    dishesByProduct[line.ProductId] = names;
                }

                names.Add(recipe.DishName);
            }
        }

        var products = await db.Products.AsNoTracking()
            .Where(product => product.IsActive)
            .OrderBy(product => product.Name)
            .ToListAsync(cancellationToken);
        var elapsed = 0d;
        var items = new List<WeeklyForecastItemView>();
        foreach (var product in products)
        {
            var used = consumed.GetValueOrDefault(product.Id);
            if (used <= 0 && !dishesByProduct.ContainsKey(product.Id))
            {
                continue;
            }

            var projection = WeeklyForecast.Project(
                used,
                weekStart,
                now,
                batchesByProduct.GetValueOrDefault(product.Id) ?? []);
            elapsed = projection.ElapsedDays;
            items.Add(new WeeklyForecastItemView(
                product.Id,
                product.Name,
                product.Unit,
                used,
                projection.ProjectedNeed,
                projection.OnHand,
                projection.Shortfall,
                dishesByProduct.GetValueOrDefault(product.Id)?.Distinct().OrderBy(name => name).ToList() ?? []));
        }

        if (items.Count == 0)
        {
            elapsed = Math.Round(Math.Max((now - weekStart).TotalDays, 1d / 24d), 2);
        }

        return new WeeklyForecastReport(
            weekStart,
            elapsed,
            ordersThisWeek,
            items.OrderByDescending(item => item.Shortfall).ThenBy(item => item.ProductName).ToList());
    }

    public async Task<IReadOnlyDictionary<Guid, double>> TypicalShelfLifeHoursAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
    {
        var batches = await db.Batches.AsNoTracking()
            .Where(batch => productIds.Contains(batch.ProductId))
            .Select(batch => new { batch.ProductId, batch.ReceivedAt, batch.ExpiresAt })
            .ToListAsync(cancellationToken);
        return batches
            .GroupBy(batch => batch.ProductId)
            .ToDictionary(
                group => group.Key,
                group => MedianHours(group.Select(batch => (batch.ExpiresAt - batch.ReceivedAt).TotalHours)));
    }

    private static double MedianHours(IEnumerable<double> hours)
    {
        var list = hours.Where(hour => hour >= 1).OrderBy(hour => hour).ToList();
        if (list.Count == 0)
        {
            return 24 * 7;
        }

        var middle = list.Count / 2;
        var median = list.Count % 2 == 1 ? list[middle] : (list[middle - 1] + list[middle]) / 2;
        return Math.Round(median, 0);
    }

    public async Task<IReadOnlyList<WriteOffView>> WriteOffsAsync(DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var rows = await db.StockMovements.AsNoTracking()
            .Include(movement => movement.Batch)
            .ThenInclude(batch => batch.Product)
            .Where(movement => movement.CreatedAt >= from
                && movement.CreatedAt < to
                && (movement.Type == MovementType.ExpiryWriteOff || movement.Type == MovementType.OrderWriteOff || movement.Type == MovementType.KitchenWriteOff))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(movement => new { movement.Batch.Product.Id, movement.Batch.Product.Name, movement.Batch.Product.Unit, movement.Type })
            .Select(group => new WriteOffView(group.Key.Id, group.Key.Name, group.Key.Unit, group.Key.Type, group.Sum(movement => movement.Quantity)))
            .OrderByDescending(row => row.Quantity)
            .ToList();
    }

    public async Task<IReadOnlyList<DeficitView>> DeficitsAsync(CancellationToken cancellationToken)
    {
        var shortages = await db.OrderShortages.AsNoTracking()
            .Include(shortage => shortage.Product)
            .ToListAsync(cancellationToken);
        var stock = await StockAsync(cancellationToken);
        var available = stock.ToDictionary(item => item.ProductId, item => item.Available);
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(product => product.Id, cancellationToken);
        var recipes = await db.DishIngredients.AsNoTracking().ToListAsync(cancellationToken);

        var byProduct = shortages
            .GroupBy(shortage => shortage.ProductId)
            .ToDictionary(
                group => group.Key,
                group => (
                    Count: group.Select(shortage => shortage.OrderId).Distinct().Count(),
                    Deficit: group.Sum(shortage => shortage.Required - shortage.Available)));

        var ids = byProduct.Keys.ToHashSet();
        foreach (var line in recipes)
        {
            var have = available.GetValueOrDefault(line.ProductId);
            if (have < line.Quantity)
            {
                ids.Add(line.ProductId);
            }
        }

        return ids
            .Where(products.ContainsKey)
            .Select(id =>
            {
                var stats = byProduct.GetValueOrDefault(id);
                var product = products[id];
                return new DeficitView(id, product.Name, product.Unit, stats.Count, stats.Deficit, available.GetValueOrDefault(id));
            })
            .OrderByDescending(row => row.RejectionCount)
            .ThenBy(row => row.Available)
            .ToList();
    }

    public async Task<IReadOnlyList<OrderView>> OrdersAsync(CancellationToken cancellationToken)
    {
        var orders = await OrderQuery()
            .OrderByDescending(order => order.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        return orders.Select(MapOrder).ToList();
    }

    public async Task<OrderView?> OrderAsync(Guid id, CancellationToken cancellationToken)
    {
        var order = await OrderQuery().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        return order is null ? null : MapOrder(order);
    }

    internal async Task<IReadOnlyList<DishRecipe>> RecipesAsync(CancellationToken cancellationToken)
    {
        var dishes = await db.Dishes.AsNoTracking()
            .Include(dish => dish.Ingredients)
            .ThenInclude(line => line.Product)
            .Where(dish => dish.IsActive)
            .ToListAsync(cancellationToken);
        return dishes.Select(dish => new DishRecipe(
            dish.Id,
            dish.Name,
            dish.Ingredients.Select(line => new RecipeLine(line.ProductId, line.Product.Name, line.Quantity)).ToList())).ToList();
    }

    internal static OrderView MapOrder(CustomerOrder order) => new(
        order.Id,
        order.CreatedAt,
        order.CreatedByUser.Login,
        order.Status,
        order.RejectionReason,
        order.Items.Select(item => new OrderLineView(item.Id, item.DishId, item.Dish.Name, item.Portions, item.KitchenStatus, item.KitchenNote)).ToList(),
        order.Shortages.Select(shortage => new ShortageView(
            shortage.ProductId,
            shortage.Product.Name,
            shortage.Required,
            shortage.Available,
            shortage.Required - shortage.Available)).ToList());

    private async Task<IReadOnlyDictionary<Guid, ProductAvailability>> AvailabilityAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var batches = await db.Batches.AsNoTracking()
            .Where(batch => batch.Status == BatchStatus.Active && batch.QuantityRemaining > 0 && batch.ExpiresAt > now)
            .ToListAsync(cancellationToken);
        return batches
            .GroupBy(batch => batch.ProductId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var ordered = group.OrderBy(batch => batch.ExpiresAt).ToList();
                    return new ProductAvailability(ordered.Sum(batch => batch.QuantityRemaining), ordered[0].ExpiresAt);
                });
    }

    private async Task<IReadOnlyList<ForecastView>> ForecastsAsync(
        IReadOnlyList<StopListEntry> stop,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var from = now.AddHours(-6);
        var movements = await db.StockMovements.AsNoTracking()
            .Include(movement => movement.Batch)
            .Where(movement => movement.Type == MovementType.OrderWriteOff && movement.CreatedAt >= from)
            .ToListAsync(cancellationToken);
        var rate = movements
            .GroupBy(movement => movement.Batch.ProductId)
            .ToDictionary(group => group.Key, group => group.Sum(movement => movement.Quantity) / 6m);
        if (rate.Count == 0)
        {
            return [];
        }

        var open = stop.Where(entry => !entry.IsStopped).Select(entry => entry.DishId).ToHashSet();
        var recipes = await RecipesAsync(cancellationToken);
        var stock = await AvailabilityAsync(cancellationToken);
        var forecasts = new List<ForecastView>();
        foreach (var recipe in recipes.Where(recipe => open.Contains(recipe.DishId)))
        {
            ForecastView? tightest = null;
            foreach (var line in recipe.Lines)
            {
                if (!rate.TryGetValue(line.ProductId, out var perHour) || perHour <= 0)
                {
                    continue;
                }

                var available = stock.GetValueOrDefault(line.ProductId)?.Quantity ?? 0m;
                if (available < line.QuantityPerPortion)
                {
                    continue;
                }

                var hours = (double)((available - line.QuantityPerPortion) / perHour);
                if (tightest is null || hours < tightest.HoursUntilStop)
                {
                    tightest = new ForecastView(recipe.DishName, line.ProductName, Math.Round(hours, 1));
                }
            }

            if (tightest is not null)
            {
                forecasts.Add(tightest);
            }
        }

        return forecasts.OrderBy(forecast => forecast.HoursUntilStop).Take(5).ToList();
    }

    private IQueryable<Batch> BatchQuery() =>
        db.Batches.AsNoTracking().Include(batch => batch.Product).Include(batch => batch.Delivery);

    private IQueryable<CustomerOrder> OrderQuery() =>
        db.Orders.AsNoTracking()
            .Include(order => order.CreatedByUser)
            .Include(order => order.Items).ThenInclude(item => item.Dish)
            .Include(order => order.Shortages).ThenInclude(shortage => shortage.Product);

    private BatchView MapBatch(Batch batch)
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

    private static bool IsAvailable(Batch batch, DateTime now) =>
        batch.Status == BatchStatus.Active && batch.QuantityRemaining > 0 && batch.ExpiresAt > now;
}
