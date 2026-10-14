using Microsoft.EntityFrameworkCore;
using Monitoring.Core;
using Monitoring.Core.Inventory;
using Monitoring.Core.Time;
using Monitoring.Infrastructure.Persistence;

namespace Monitoring.Infrastructure.Services;

/// <summary>Чтение для экранов. Ничего не записывает: остатки, плитки смены, заказы, аналитика.</summary>
public sealed class StockReadModel(AppDbContext db, IClock clock)
{
    /// <summary>Остаток по активным продуктам: сумма живых партий, ближайший срок, свежесть.</summary>
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
                FreshnessRules.ForProduct(available, nearest, anyExpired, now),
                product.PurchasePrice);
        }).ToList();
    }

    /// <summary>Партии одного продукта, от ближайшего срока. Вход: productId.</summary>
    public async Task<IReadOnlyList<BatchView>> BatchesAsync(Guid productId, CancellationToken cancellationToken)
    {
        var batches = await BatchQuery()
            .Where(batch => batch.ProductId == productId)
            .OrderBy(batch => batch.ExpiresAt)
            .ToListAsync(cancellationToken);
        return batches.Select(MapBatch).ToList();
    }

    /// <summary>
    /// Все блюда с рецептом. Закрытая смена — стоп «Смена закрыта».
    /// Открытая: стоп от шефа, нуля заготовки или склада. Кандидат — склада хватит на 1–5 порций, зал ещё открыт.
    /// </summary>
    public async Task<IReadOnlyList<ShiftTile>> StopListAsync(CancellationToken cancellationToken)
    {
        var recipes = await RecipesAsync(cancellationToken);
        var stock = await AvailabilityAsync(cancellationToken);
        var calculated = StopListCalculator.Calculate(recipes, stock)
            .ToDictionary(entry => entry.DishId);
        var shiftOpen = await db.ServiceShifts.AsNoTracking().AnyAsync(row => row.ClosedAt == null, cancellationToken);
        var shift = await db.ShiftDishes.AsNoTracking().ToDictionaryAsync(row => row.DishId, cancellationToken);
        var tiles = new List<ShiftTile>();
        foreach (var recipe in recipes.Where(recipe => recipe.Lines.Count > 0))
        {
            if (!shiftOpen)
            {
                tiles.Add(new ShiftTile(recipe.DishId, recipe.DishName, 0, true, false, false, false, "Смена закрыта", null, null, true, null, null));
                continue;
            }

            calculated.TryGetValue(recipe.DishId, out var stockState);
            shift.TryGetValue(recipe.DishId, out var state);
            var prep = state?.PrepPortions;
            var stockPortions = stockState is null || stockState.IsStopped ? 0 : stockState.PortionsPossible;
            var chefStop = state?.IsStopped == true;
            var prepMissing = prep is null;
            var prepOut = prep == 0;
            var stockOut = stockPortions <= 0;
            var stopped = chefStop || prepMissing || prepOut || stockOut;
            var portions = stopped ? 0 : prep!.Value;

            var low = !stopped && prep is > 0 and <= 5;
            var almost = !stopped && stockPortions is > 0 and <= 5;
            var (reason, stopReason, stopNote, stopSource) = DescribeStop(state, stockState, chefStop, prepMissing, prepOut, stockOut, low);

            tiles.Add(new ShiftTile(
                recipe.DishId,
                recipe.DishName,
                portions,
                stopped,
                almost,
                low,
                (state?.IsPriority ?? false) && !stopped,
                reason,
                almost ? $"Склада хватит на {stockPortions} порц." : stockOut ? stockState?.Reason : null,
                stopReason,
                true,
                stopNote,
                stopSource));
        }

        return tiles.OrderBy(tile => tile.DishName).ToList();
    }

    /// <summary>Только плитки, где шеф включил «предлагать» и блюдо не в стопе.</summary>
    public async Task<IReadOnlyList<ShiftTile>> PriorityAsync(CancellationToken cancellationToken) =>
        (await StopListAsync(cancellationToken)).Where(tile => tile.IsPriority).ToList();

    private static (string? Reason, string? StopReason, string? StopNote, StopSource? Source) DescribeStop(
        ShiftDish? state,
        StopListEntry? stockState,
        bool chefStop,
        bool prepMissing,
        bool prepOut,
        bool stockOut,
        bool low)
    {
        if (chefStop)
        {
            var label = ReasonLabel(state?.StopReason);
            var note = string.IsNullOrWhiteSpace(state?.StopNote) ? null : state!.StopNote;
            var reason = note is null ? label : string.IsNullOrEmpty(label) ? note : $"{label} — {note}";
            return (reason, state?.StopReason, note, StopSource.Chef);
        }

        if (prepMissing)
        {
            return ("Заготовка не задана", nameof(StopReason.OutOfProduct), null, StopSource.Prep);
        }

        if (prepOut)
        {
            var note = string.IsNullOrWhiteSpace(state?.StopNote) ? null : state!.StopNote;
            return (note ?? "Нет порций на заготовке", nameof(StopReason.OutOfProduct), note, StopSource.Prep);
        }

        if (stockOut)
        {
            var reason = stockState?.Reason ?? "Не хватает ингредиентов на порцию";
            return (reason, nameof(StopReason.OutOfProduct), null, StopSource.Stock);
        }

        if (low)
        {
            return ("Мало заготовки", null, null, null);
        }

        return (null, null, null, null);
    }

    private static string? ReasonLabel(string? reason) => reason switch
    {
        nameof(StopReason.OutOfProduct) => "Закончился продукт",
        nameof(StopReason.Waste) => "Брак",
        nameof(StopReason.Quality) => "Качество",
        nameof(StopReason.WontMakeIt) => "Не успеем",
        nameof(StopReason.Equipment) => "Техника",
        _ => reason,
    };

    /// <summary>Подсказка алгоритма для шефа. Зал этот список не показывает.</summary>
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

    /// <summary>
    /// Сводка менеджера. Число стопов — шеф, ноль заготовки и автостоп склада.
    /// Рядом партии на сутки, автосписание срока за сегодня, заказы и короткий прогноз.
    /// </summary>
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
            .Include(movement => movement.Batch)
            .ThenInclude(batch => batch.Delivery)
            .Where(movement => movement.Type == MovementType.ExpiryWriteOff && movement.CreatedAt >= now.AddDays(-7))
            .OrderByDescending(movement => movement.CreatedAt)
            .ToListAsync(cancellationToken);
        var expiryTotals = writeOffs
            .Where(movement => movement.CreatedAt >= dayStart)
            .GroupBy(movement => movement.Batch.ProductId)
            .Select(group =>
            {
                var product = group.First().Batch.Product;
                return new ExpiryTotalView(product.Id, product.Name, product.Unit, group.Sum(movement => movement.Quantity));
            })
            .OrderByDescending(row => row.Quantity)
            .ToList();
        var expired = writeOffs.Select(movement => new ExpiryWriteOffLineView(
            movement.BatchId,
            movement.Batch.ProductId,
            movement.Batch.Product.Name,
            movement.Batch.Product.Unit,
            movement.Batch.Delivery.Number,
            movement.Quantity,
            movement.Batch.ExpiresAt,
            movement.CreatedAt,
            decimal.Round(movement.Quantity * movement.Batch.UnitCost, 2))).ToList();
        var acceptedToday = await db.Orders.CountAsync(
            order => order.Status == OrderStatus.Accepted && order.CreatedAt >= dayStart,
            cancellationToken);

        return new DashboardView(
            expiring.Select(MapBatch).ToList(),
            stop.Count(entry => entry.IsStopped),
            expiryTotals,
            acceptedToday,
            await ForecastsAsync(stop, cancellationToken),
            await WeeklyForecastAsync(cancellationToken),
            expired);
    }

    /// <summary>Прогноз на следующую неделю по темпу заказов с понедельника.</summary>
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

    /// <summary>Списания за интервал [from, to). reason фильтрует статью, пусто — все.</summary>
    public async Task<IReadOnlyList<WriteOffView>> WriteOffsAsync(DateTime from, DateTime to, string? reason, CancellationToken cancellationToken)
    {
        var rows = await db.StockMovements.AsNoTracking()
            .Include(movement => movement.Batch)
            .ThenInclude(batch => batch.Product)
            .Where(movement => movement.CreatedAt >= from
                && movement.CreatedAt < to
                && (movement.Type == MovementType.ExpiryWriteOff || movement.Type == MovementType.OrderWriteOff || movement.Type == MovementType.KitchenWriteOff))
            .ToListAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(reason))
        {
            rows = rows.Where(movement => string.Equals(movement.Reason, reason, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return rows
            .GroupBy(movement => new { movement.Batch.Product.Id, movement.Batch.Product.Name, movement.Batch.Product.Unit, movement.Type, movement.Reason })
            .Select(group => new WriteOffView(group.Key.Id, group.Key.Name, group.Key.Unit, group.Key.Type, group.Sum(movement => movement.Quantity), group.Key.Reason))
            .OrderByDescending(row => row.Quantity)
            .ToList();
    }

    /// <summary>По отказам заказов: сколько раз не хватило продукта и сколько не хватает сейчас.</summary>
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

    /// <summary>Заказы с строками, дефицитом и номером стола, новые сверху.</summary>
    public async Task<IReadOnlyList<OrderView>> OrdersAsync(CancellationToken cancellationToken)
    {
        var orders = await OrderQuery()
            .OrderByDescending(order => order.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        return orders.Select(MapOrder).ToList();
    }

    /// <summary>Один заказ по id или null, если такого нет.</summary>
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
            shortage.Required - shortage.Available)).ToList(),
        order.TableNumber);

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
        IReadOnlyList<ShiftTile> stop,
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
