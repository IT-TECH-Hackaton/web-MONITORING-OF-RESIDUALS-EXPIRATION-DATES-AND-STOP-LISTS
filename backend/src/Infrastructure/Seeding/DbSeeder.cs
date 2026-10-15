using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Monitoring.Core;
using Monitoring.Core.Time;
using Monitoring.Infrastructure.Auth;
using Monitoring.Infrastructure.Persistence;

namespace Monitoring.Infrastructure.Seeding;

/// <summary>
/// Первый запуск: пользователи, продукты, партии и техкарты.
/// Если склад уже есть, дописывает только смену демо-блюд, когда таблица порций пустая.
/// Seed:DemoData false — демо не кладёт, так работают тесты.
/// </summary>
public sealed class DbSeeder(AppDbContext db, IConfiguration configuration, IClock clock)
{
    public static readonly Guid SalmonId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid MozzarellaId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public static readonly Guid TomatoId = Guid.Parse("10000000-0000-0000-0000-000000000003");
    public static readonly Guid BasilId = Guid.Parse("10000000-0000-0000-0000-000000000004");
    public static readonly Guid PastaId = Guid.Parse("10000000-0000-0000-0000-000000000005");
    public static readonly Guid CreamId = Guid.Parse("10000000-0000-0000-0000-000000000006");
    public static readonly Guid CoffeeId = Guid.Parse("10000000-0000-0000-0000-000000000007");
    public static readonly Guid MilkId = Guid.Parse("10000000-0000-0000-0000-000000000008");

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (!await db.Users.AnyAsync(cancellationToken))
        {
            db.Users.AddRange(
                User("manager", "manager123", UserRole.Manager),
                User("waiter", "waiter123", UserRole.Waiter));
        }

        if (!await db.Users.AnyAsync(user => user.Login == "kitchen", cancellationToken))
        {
            db.Users.Add(User("kitchen", "kitchen123", UserRole.Kitchen));
        }

        await db.SaveChangesAsync(cancellationToken);

        var demoData = !string.Equals(configuration["Seed:DemoData"], "false", StringComparison.OrdinalIgnoreCase);
        if (!demoData)
        {
            return;
        }

        if (await db.Products.AnyAsync(cancellationToken))
        {
            await EnsureMoneyAsync(cancellationToken);
            await SeedShiftAsync(cancellationToken);
            await SeedPurchaseLinesAsync(cancellationToken);
            await SeedShowcaseAsync(cancellationToken);
            await SeedFillAsync(cancellationToken);
            return;
        }

        db.Products.AddRange(
            Product(SalmonId, "Лосось", Unit.G),
            Product(MozzarellaId, "Моцарелла", Unit.G),
            Product(TomatoId, "Томаты", Unit.G),
            Product(BasilId, "Базилик", Unit.G),
            Product(PastaId, "Спагетти", Unit.G),
            Product(CreamId, "Сливки", Unit.Ml),
            Product(CoffeeId, "Кофе", Unit.G),
            Product(MilkId, "Молоко", Unit.Ml));

        await db.SaveChangesAsync(cancellationToken);
        await EnsureMoneyAsync(cancellationToken);
        await SeedShiftAsync(cancellationToken);
        await SeedPurchaseLinesAsync(cancellationToken);
        await SeedShowcaseAsync(cancellationToken);
        await SeedFillAsync(cancellationToken);
    }

    private async Task EnsureMoneyAsync(CancellationToken cancellationToken)
    {
        await EnsureUserAsync("waiter2", "waiter2123", UserRole.Waiter, "Пётр", cancellationToken);
        await EnsureUserAsync("kitchen2", "kitchen2123", UserRole.Kitchen, "Олег", cancellationToken);
        await EnsureUserAsync("buyer", "buyer123", UserRole.Manager, "Елена", cancellationToken);
        await EnsureUserAsync("store", "store123", UserRole.Kitchen, "Сергей", cancellationToken);
        await EnsureUserAsync("supply", "supply123", UserRole.Supply, "Поставка", cancellationToken);
        await NameAsync("manager", "Анна", cancellationToken);
        await NameAsync("kitchen", "Игорь", cancellationToken);
        await NameAsync("waiter", "Мария", cancellationToken);

        foreach (var item in MoneyCatalog.Products)
        {
            var product = await db.Products.FirstOrDefaultAsync(row => row.Id == item.Id, cancellationToken);
            if (product is null)
            {
                db.Products.Add(new Product { Id = item.Id, Name = item.Name, Unit = item.Unit, PurchasePrice = item.Price, IsActive = true });
            }
            else
            {
                product.PurchasePrice = item.Price;
            }
        }

        foreach (var item in MoneyCatalog.Dishes)
        {
            var dish = await db.Dishes.Include(row => row.Ingredients).FirstOrDefaultAsync(row => row.Id == item.Id, cancellationToken);
            if (dish is null)
            {
                db.Dishes.Add(new Dish
                {
                    Id = item.Id,
                    Name = item.Name,
                    SalePrice = item.Sale,
                    IsActive = true,
                    Ingredients = item.Lines.Select(line => new DishIngredient { DishId = item.Id, ProductId = line.ProductId, Quantity = line.Quantity }).ToList(),
                });
            }
            else
            {
                dish.SalePrice = item.Sale;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (var item in MoneyCatalog.Products)
        {
            await db.Batches.Where(batch => batch.ProductId == item.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(batch => batch.UnitCost, item.Price), cancellationToken);
        }

        var buyer = await db.Users.FirstAsync(user => user.Login == "buyer", cancellationToken);
        var chef = await db.Users.FirstAsync(user => user.Login == "kitchen", cancellationToken);
        var store = await db.Users.FirstAsync(user => user.Login == "store", cancellationToken);
        var manager = await db.Users.FirstAsync(user => user.Login == "manager", cancellationToken);
        await ReplaceDemoInvoicesAsync(manager.Id, buyer.Id, chef.Id, store.Id, cancellationToken);
    }

    private async Task ReplaceDemoInvoicesAsync(Guid managerId, Guid buyerId, Guid chefId, Guid storeId, CancellationToken cancellationToken)
    {
        var seedIds = MoneyCatalog.Invoices.Select(item => item.Id).ToHashSet();
        var old = await db.Deliveries
            .Include(delivery => delivery.Batches)
            .ThenInclude(batch => batch.Movements)
            .Where(delivery => seedIds.Contains(delivery.Id)
                || delivery.Number.StartsWith("П-20")
                || delivery.Number == "П-1001"
                || delivery.Number == "П-1002"
                || delivery.Number == "П-1108"
                || delivery.Number == "П-1114"
                || delivery.Number == "П-1120")
            .ToListAsync(cancellationToken);
        if (old.Count > 0)
        {
            db.Deliveries.RemoveRange(old);
            await db.SaveChangesAsync(cancellationToken);
        }

        var now = clock.UtcNow;
        var receivers = new Dictionary<Guid, Guid>
        {
            [MoneyCatalog.MetroId] = managerId,
            [MoneyCatalog.DairyPrevId] = buyerId,
            [MoneyCatalog.FishMainId] = buyerId,
            [MoneyCatalog.MeatId] = buyerId,
            [MoneyCatalog.DairyId] = storeId,
            [MoneyCatalog.FishShortId] = chefId,
            [MoneyCatalog.VegId] = chefId,
            [MoneyCatalog.ExpiredVegId] = chefId,
        };

        foreach (var invoice in MoneyCatalog.Invoices)
        {
            var at = DateTime.SpecifyKind(
                now.Date.AddDays(-invoice.DaysAgo).AddHours(invoice.Hour).AddMinutes(invoice.Minute),
                DateTimeKind.Utc);
            if (at > now)
            {
                at = now.AddMinutes(-40);
            }

            var batches = invoice.Lines.Select(line => Line(
                line.ProductId,
                line.Quantity,
                at.AddDays(line.DaysUntilExpiry).AddHours(line.ExtraHours))).ToArray();
            db.Deliveries.Add(PricedDelivery(
                invoice.Id,
                invoice.Number,
                at,
                receivers.GetValueOrDefault(invoice.Id, managerId),
                invoice.Supplier,
                batches));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureUserAsync(string login, string password, UserRole role, string name, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(row => row.Login == login, cancellationToken);
        if (user is null)
        {
            db.Users.Add(new UserAccount { Id = Guid.NewGuid(), Login = login, DisplayName = name, PasswordHash = PasswordHasher.Hash(password), Role = role });
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(user.DisplayName))
        {
            user.DisplayName = name;
        }
    }

    private async Task NameAsync(string login, string name, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(row => row.Login == login, cancellationToken);
        if (user is not null && string.IsNullOrWhiteSpace(user.DisplayName))
        {
            user.DisplayName = name;
        }
    }

    private Delivery PricedDelivery(Guid id, string number, DateTime at, Guid userId, string supplier, params Batch[] batches)
    {
        var delivery = new Delivery { Id = id, Number = number, Supplier = supplier, CreatedAt = at, AcceptedAt = at, ReceivedBy = userId, Batches = batches.ToList() };
        foreach (var batch in batches)
        {
            batch.DeliveryId = id;
            batch.ReceivedAt = at;
            batch.Movements.Add(new StockMovement
            {
                Id = Guid.NewGuid(),
                Type = MovementType.Receipt,
                Quantity = batch.QuantityInitial,
                CreatedAt = at,
            });
        }

        return delivery;
    }

    private static Batch Line(Guid productId, decimal quantity, DateTime expiresAt)
    {
        var price = MoneyCatalog.Products.First(item => item.Id == productId).Price;
        return new Batch
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            QuantityInitial = quantity,
            QuantityRemaining = quantity,
            ExpiresAt = expiresAt,
            Status = BatchStatus.Active,
            UnitCost = price,
        };
    }

    private async Task SeedShiftAsync(CancellationToken cancellationToken)
    {
        if (!await db.Dishes.AnyAsync(dish => dish.Id == MoneyCatalog.PastaDishId, cancellationToken))
        {
            return;
        }

        if (await db.ServiceShifts.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = clock.UtcNow;
        var chef = await db.Users.FirstOrDefaultAsync(user => user.Login == "kitchen", cancellationToken)
            ?? await db.Users.FirstOrDefaultAsync(cancellationToken);
        if (chef is null)
        {
            return;
        }

        var fresh = !await db.ShiftDishes.AnyAsync(cancellationToken);
        db.ServiceShifts.Add(new ServiceShift
        {
            Id = Guid.NewGuid(),
            OpenedAt = now.AddHours(-6),
            OpenedBy = chef.Id,
        });
        if (!fresh)
        {
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        await UpsertShiftAsync(MoneyCatalog.PastaDishId, 3, false, true, null, null, cancellationToken);
        await UpsertShiftAsync(MoneyCatalog.CapreseDishId, 4, false, true, null, null, cancellationToken);
        await UpsertShiftAsync(MoneyCatalog.PizzaDishId, 0, true, false, nameof(StopReason.Quality), "Тесто не прошло", cancellationToken);
        await UpsertShiftAsync(MoneyCatalog.CoffeeDishId, 0, true, false, nameof(StopReason.Equipment), "Кофемашина", cancellationToken);
        await UpsertShiftAsync(MoneyCatalog.CheesecakeDishId, 1, false, false, null, null, cancellationToken);

        if (fresh)
        {
            db.ShiftHistory.AddRange(
                History(MoneyCatalog.PizzaDishId, "шеф", "Стоп: качество — тесто не прошло", now.AddHours(-5)),
                History(MoneyCatalog.CoffeeDishId, "су", "Стоп: техника — кофемашина", now.AddHours(-2)),
                History(MoneyCatalog.PastaDishId, "шеф", "В приоритет, заготовки 3", now.AddHours(-1)),
                History(MoneyCatalog.CapreseDishId, "шеф", "В приоритет, заготовки 4", now.AddHours(-1)),
                History(MoneyCatalog.CheesecakeDishId, "шеф", "Заготовка 1 порция — после заказа уйдёт в стоп", now.AddMinutes(-20)));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task UpsertShiftAsync(
        Guid dishId,
        int portions,
        bool stopped,
        bool priority,
        string? reason,
        string? note,
        CancellationToken cancellationToken)
    {
        if (!await db.Dishes.AnyAsync(dish => dish.Id == dishId, cancellationToken))
        {
            return;
        }

        var row = await db.ShiftDishes.FirstOrDefaultAsync(item => item.DishId == dishId, cancellationToken);
        var chef = await db.Users.FirstOrDefaultAsync(user => user.Login == "kitchen", cancellationToken);
        if (row is null)
        {
            db.ShiftDishes.Add(new ShiftDish
            {
                DishId = dishId,
                PrepPortions = portions,
                IsStopped = stopped,
                IsPriority = priority,
                StopReason = reason,
                StopNote = note,
                StoppedAt = stopped ? clock.UtcNow.AddHours(-2) : null,
                StoppedBy = stopped ? chef?.Id : null,
            });
            return;
        }

        row.PrepPortions = portions;
        row.IsStopped = stopped;
        row.IsPriority = priority;
        row.StopReason = reason;
        row.StopNote = note;
        if (stopped)
        {
            row.StoppedAt ??= clock.UtcNow.AddHours(-2);
            row.StoppedBy ??= chef?.Id;
        }
        else
        {
            row.StoppedAt = null;
            row.StoppedBy = null;
        }
    }

    private async Task SeedPurchaseLinesAsync(CancellationToken cancellationToken)
    {
        if (await db.PurchaseLines.AnyAsync(cancellationToken))
        {
            return;
        }

        var chef = await db.Users.FirstOrDefaultAsync(user => user.Login == "kitchen", cancellationToken);
        if (chef is null)
        {
            return;
        }

        var now = clock.UtcNow;
        db.PurchaseLines.AddRange(
            new PurchaseLine
            {
                Id = Guid.Parse("40000000-0000-0000-0000-000000000001"),
                ProductId = CreamId,
                Quantity = 2000,
                Note = "на банкет в пятницу",
                Source = PurchaseSource.Chef,
                RequestedBy = chef.Id,
                CreatedAt = now.AddHours(-3),
            },
            new PurchaseLine
            {
                Id = Guid.Parse("40000000-0000-0000-0000-000000000002"),
                ProductId = MozzarellaId,
                Quantity = 800,
                Note = "вчерашняя партия слабая, возьми другую",
                Source = PurchaseSource.Chef,
                RequestedBy = chef.Id,
                CreatedAt = now.AddHours(-1),
            });
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedShowcaseAsync(CancellationToken cancellationToken)
    {
        var marker = Guid.Parse("50000000-0000-0000-0000-000000000001");
        if (await db.Orders.AnyAsync(order => order.Id == marker, cancellationToken))
        {
            return;
        }

        var now = clock.UtcNow;
        var manager = await db.Users.FirstAsync(user => user.Login == "manager", cancellationToken);
        var buyer = await db.Users.FirstAsync(user => user.Login == "buyer", cancellationToken);
        var chef = await db.Users.FirstAsync(user => user.Login == "kitchen", cancellationToken);
        var store = await db.Users.FirstAsync(user => user.Login == "store", cancellationToken);
        var waiter = await db.Users.FirstAsync(user => user.Login == "waiter", cancellationToken);
        var waiter2 = await db.Users.FirstOrDefaultAsync(user => user.Login == "waiter2", cancellationToken) ?? waiter;
        var kitchen2 = await db.Users.FirstOrDefaultAsync(user => user.Login == "kitchen2", cancellationToken) ?? chef;
        var people = new[] { manager.Id, buyer.Id, store.Id, chef.Id };
        var suppliers = new[] { "Метро Кеш энд Керри", "Северная рыба", "Мясной двор", "Овощи Юг", "Молочный двор" };
        (Guid Product, decimal Qty, int Life)[] samples =
        [
            (SalmonId, 600, 5),
            (CoffeeId, 400, 90),
            (MozzarellaId, 900, 10),
            (MoneyCatalog.Beef, 1200, 5),
            (MoneyCatalog.Chicken, 1800, 4),
            (MoneyCatalog.Shrimp, 500, 3),
            (CreamId, 1500, 6),
            (MoneyCatalog.Flour, 6000, 80),
            (MilkId, 4000, 5),
            (TomatoId, 2500, 6),
        ];

        for (var ago = 9; ago <= 28; ago++)
        {
            if (ago % 7 == 0)
            {
                continue;
            }

            var sample = samples[ago % samples.Length];
            var at = DateTime.SpecifyKind(now.Date.AddDays(-ago).AddHours(8 + (ago % 8)), DateTimeKind.Utc);
            db.Deliveries.Add(PricedDelivery(
                Guid.Parse($"31000000-0000-0000-0000-{ago:000000000000}"),
                $"ДЕМО-{ago:00}",
                at,
                people[ago % people.Length],
                suppliers[ago % suppliers.Length],
                Line(sample.Product, sample.Qty, at.AddDays(sample.Life))));
        }

        db.Deliveries.Add(PricedDelivery(
            Guid.Parse("31000000-0000-0000-0000-000000000030"),
            "ДЕМО-СРОК",
            now.AddHours(-6),
            chef.Id,
            "Северная рыба",
            Line(SalmonId, 350, now.AddHours(20))));

        db.ServiceShifts.AddRange(
            ClosedShift("32000000-0000-0000-0000-000000000001", now.AddDays(-6).AddHours(8), now.AddDays(-6).AddHours(22), chef.Id, manager.Id),
            ClosedShift("32000000-0000-0000-0000-000000000002", now.AddDays(-4).AddHours(9), now.AddDays(-4).AddHours(23), store.Id, chef.Id),
            ClosedShift("32000000-0000-0000-0000-000000000003", now.AddDays(-2).AddHours(8), now.AddDays(-1).AddHours(1), kitchen2.Id, chef.Id));

        await db.SaveChangesAsync(cancellationToken);

        async Task Sell(string orderId, UserAccount who, string table, Guid dishId, int portions, int daysAgo, int hour, Guid productId, decimal writeOffQty)
        {
            var at = DateTime.SpecifyKind(now.Date.AddDays(-daysAgo).AddHours(hour), DateTimeKind.Utc);
            var orderGuid = Guid.Parse(orderId);
            var order = new CustomerOrder
            {
                Id = orderGuid,
                CreatedAt = at,
                CreatedBy = who.Id,
                Status = OrderStatus.Accepted,
                TableNumber = table,
                Items =
                [
                    new OrderItem { Id = Guid.NewGuid(), OrderId = orderGuid, DishId = dishId, Portions = portions, KitchenStatus = KitchenStatus.Ready },
                ],
            };
            db.Orders.Add(order);
            var batch = await db.Batches.FirstAsync(row => row.ProductId == productId && row.QuantityRemaining >= writeOffQty && row.Status == BatchStatus.Active, cancellationToken);
            batch.QuantityRemaining -= writeOffQty;
            db.StockMovements.Add(new StockMovement
            {
                Id = Guid.NewGuid(),
                BatchId = batch.Id,
                OrderId = orderGuid,
                Type = MovementType.OrderWriteOff,
                Quantity = writeOffQty,
                CreatedAt = at,
            });
        }

        await Sell("50000000-0000-0000-0000-000000000001", waiter, "4", MoneyCatalog.PastaDishId, 2, 1, 13, SalmonId, 360);
        await Sell("50000000-0000-0000-0000-000000000002", waiter, "7", Guid.Parse("20000000-0000-0000-0000-000000000005"), 3, 1, 19, MoneyCatalog.Chicken, 300);
        await Sell("50000000-0000-0000-0000-000000000003", waiter2, "2", Guid.Parse("20000000-0000-0000-0000-000000000006"), 4, 2, 14, MoneyCatalog.Beef, 320);
        await Sell("50000000-0000-0000-0000-000000000004", waiter, "11", Guid.Parse("20000000-0000-0000-0000-000000000007"), 2, 2, 20, MoneyCatalog.Shrimp, 200);
        await Sell("50000000-0000-0000-0000-000000000005", waiter2, "5", Guid.Parse("20000000-0000-0000-0000-000000000008"), 2, 3, 18, MoneyCatalog.Beef, 240);
        await Sell("50000000-0000-0000-0000-000000000006", waiter, "9", MoneyCatalog.CheesecakeDishId, 3, 3, 21, CreamId, 240);
        await Sell("50000000-0000-0000-0000-000000000007", waiter2, "1", MoneyCatalog.CoffeeDishId, 6, 0, 11, CoffeeId, 108);

        var rejectedId = Guid.Parse("50000000-0000-0000-0000-000000000008");
        db.Orders.Add(new CustomerOrder
        {
            Id = rejectedId,
            CreatedAt = now.AddHours(-5),
            CreatedBy = waiter.Id,
            Status = OrderStatus.Rejected,
            TableNumber = "8",
            RejectionReason = "Базилик: не хватает 40 г",
            Shortages =
            [
                new OrderShortage
                {
                    Id = Guid.NewGuid(),
                    OrderId = rejectedId,
                    ProductId = BasilId,
                    Required = 48,
                    Available = 8,
                },
            ],
        });

        async Task Waste(Guid productId, decimal quantity, string reason, MovementType type, int daysAgo)
        {
            var batch = await db.Batches.FirstOrDefaultAsync(row => row.ProductId == productId && row.QuantityRemaining >= quantity && row.Status == BatchStatus.Active, cancellationToken);
            if (batch is null)
            {
                return;
            }

            batch.QuantityRemaining -= quantity;
            db.StockMovements.Add(new StockMovement
            {
                Id = Guid.NewGuid(),
                BatchId = batch.Id,
                Type = type,
                Reason = reason,
                Quantity = quantity,
                CreatedAt = now.AddDays(-daysAgo).AddHours(15),
            });
        }

        await Waste(TomatoId, 400, nameof(WriteOffReason.Waste), MovementType.KitchenWriteOff, 1);
        await Waste(MoneyCatalog.Cucumber, 250, nameof(WriteOffReason.Spoilage), MovementType.KitchenWriteOff, 2);
        await Waste(MoneyCatalog.Beef, 180, nameof(WriteOffReason.Training), MovementType.KitchenWriteOff, 4);
        await Waste(MoneyCatalog.Chicken, 350, nameof(WriteOffReason.StaffMeal), MovementType.KitchenWriteOff, 3);

        if (!await db.PurchaseLines.AnyAsync(line => line.Id == Guid.Parse("40000000-0000-0000-0000-000000000003"), cancellationToken))
        {
            db.PurchaseLines.AddRange(
                new PurchaseLine
                {
                    Id = Guid.Parse("40000000-0000-0000-0000-000000000003"),
                    ProductId = BasilId,
                    Quantity = 200,
                    Note = "прогноз: на следующей неделе не хватит",
                    Source = PurchaseSource.Forecast,
                    RequestedBy = manager.Id,
                    CreatedAt = now.AddHours(-4),
                },
                new PurchaseLine
                {
                    Id = Guid.Parse("40000000-0000-0000-0000-000000000004"),
                    ProductId = SalmonId,
                    Quantity = 3000,
                    Note = "банкет в субботу, 40 порций",
                    Source = PurchaseSource.Manager,
                    RequestedBy = buyer.Id,
                    CreatedAt = now.AddDays(-1),
                });
        }

        var milk = await db.Batches.FirstOrDefaultAsync(batch => batch.ProductId == MilkId && batch.OpenedAt == null && batch.QuantityRemaining > 0, cancellationToken);
        if (milk is not null)
        {
            milk.OpenedAt = now.AddHours(-5);
            var openedUntil = milk.OpenedAt.Value.AddHours(24);
            if (openedUntil < milk.ExpiresAt)
            {
                milk.ExpiresAt = openedUntil;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Плотная витрина: поставки, заказы со списанием, акты, заказ поставщику, чат. Повторно не пишется.</summary>
    private async Task SeedFillAsync(CancellationToken cancellationToken)
    {
        var marker = Guid.Parse("51000000-0000-0000-0000-000000000001");
        if (await db.Orders.AnyAsync(order => order.Id == marker, cancellationToken))
        {
            return;
        }

        var now = clock.UtcNow;
        var manager = await db.Users.FirstAsync(user => user.Login == "manager", cancellationToken);
        var buyer = await db.Users.FirstAsync(user => user.Login == "buyer", cancellationToken);
        var chef = await db.Users.FirstAsync(user => user.Login == "kitchen", cancellationToken);
        var store = await db.Users.FirstAsync(user => user.Login == "store", cancellationToken);
        var waiter = await db.Users.FirstAsync(user => user.Login == "waiter", cancellationToken);
        var waiter2 = await db.Users.FirstOrDefaultAsync(user => user.Login == "waiter2", cancellationToken) ?? waiter;
        var kitchen2 = await db.Users.FirstOrDefaultAsync(user => user.Login == "kitchen2", cancellationToken) ?? chef;
        var supply = await db.Users.FirstOrDefaultAsync(user => user.Login == "supply", cancellationToken);
        var suppliers = new[] { "Метро Кеш энд Керри", "Северная рыба", "Мясной двор", "Овощи Юг", "Молочный двор", "Бакалея Дом" };
        (Guid Product, decimal Qty, int Life)[] packs =
        [
            (SalmonId, 1800, 4), (MozzarellaId, 2200, 12), (TomatoId, 4000, 5), (BasilId, 300, 4),
            (PastaId, 5000, 120), (CreamId, 3000, 7), (CoffeeId, 1600, 90), (MilkId, 8000, 6),
            (MoneyCatalog.Chicken, 3500, 4), (MoneyCatalog.Beef, 2800, 5), (MoneyCatalog.Shrimp, 900, 3),
            (MoneyCatalog.Potato, 12000, 20), (MoneyCatalog.Lemon, 1500, 12), (MoneyCatalog.Lettuce, 800, 3),
            (MoneyCatalog.Flour, 10000, 90), (MoneyCatalog.Egg, 120, 18),
        ];
        var receivers = new[] { manager.Id, buyer.Id, chef.Id, store.Id };

        for (var day = 1; day <= 18; day++)
        {
            var pack = packs[day % packs.Length];
            var extra = packs[(day + 5) % packs.Length];
            var at = DateTime.SpecifyKind(now.Date.AddDays(-day).AddHours(7 + (day % 6)), DateTimeKind.Utc);
            db.Deliveries.Add(PricedDelivery(
                Guid.Parse($"33000000-0000-0000-0000-{day:000000000000}"),
                $"НАБ-{day:00}",
                at,
                receivers[day % receivers.Length],
                suppliers[day % suppliers.Length],
                Line(pack.Product, pack.Qty, at.AddDays(pack.Life)),
                Line(extra.Product, Math.Max(1, extra.Qty / 2), at.AddDays(extra.Life))));
        }

        db.Deliveries.Add(new Delivery
        {
            Id = Guid.Parse("33000000-0000-0000-0000-000000000099"),
            Number = "НАБ-МАШИНА",
            Supplier = "Северная рыба",
            CreatedAt = now.AddHours(-2),
            Stage = DeliveryStage.Parked,
            Batches =
            [
                Parked(Guid.Parse("33000000-0000-0000-0000-000000000099"), SalmonId, 1200, now.AddDays(2)),
                Parked(Guid.Parse("33000000-0000-0000-0000-000000000099"), CreamId, 2000, now.AddDays(5)),
                Parked(Guid.Parse("33000000-0000-0000-0000-000000000099"), BasilId, 150, now.AddDays(3)),
            ],
        });

        db.Deliveries.Add(PricedDelivery(
            Guid.Parse("33000000-0000-0000-0000-000000000098"),
            "НАБ-ПОРЧА",
            now.AddDays(-1).AddHours(6),
            chef.Id,
            "Овощи Юг",
            new Batch
            {
                Id = Guid.NewGuid(),
                ProductId = MoneyCatalog.Lettuce,
                QuantityInitial = 600,
                QuantityRemaining = 420,
                ExpiresAt = now.AddHours(-3),
                Status = BatchStatus.PendingWriteOff,
                UnitCost = MoneyCatalog.Products.First(item => item.Id == MoneyCatalog.Lettuce).Price,
                ReceivedAt = now.AddDays(-1).AddHours(6),
            }));

        db.ServiceShifts.AddRange(
            ClosedShift("34000000-0000-0000-0000-000000000001", now.AddDays(-12).AddHours(8), now.AddDays(-12).AddHours(23), chef.Id, manager.Id),
            ClosedShift("34000000-0000-0000-0000-000000000002", now.AddDays(-10).AddHours(9), now.AddDays(-10).AddHours(22), kitchen2.Id, chef.Id),
            ClosedShift("34000000-0000-0000-0000-000000000003", now.AddDays(-8).AddHours(8), now.AddDays(-8).AddHours(21), store.Id, manager.Id),
            ClosedShift("34000000-0000-0000-0000-000000000004", now.AddDays(-5).AddHours(10), now.AddDays(-5).AddHours(23), chef.Id, buyer.Id),
            ClosedShift("34000000-0000-0000-0000-000000000005", now.AddDays(-3).AddHours(8), now.AddDays(-3).AddHours(20), kitchen2.Id, chef.Id));

        var notes = new[]
        {
            (chef, "Лосось на банкет режем после шести, не раньше."),
            (waiter, "Стол 12 просит без базилика."),
            (manager, "Молоко на утро возьмите из левой камеры."),
            (store, "Картофель приехал, ящик у чёрного хода."),
            (waiter2, "Стол 3 добавил ещё два капучино."),
            (kitchen2, "Креветки короткие по сроку, в приоритет."),
            (buyer, "Сыр на завтра подтвердили, машина к десяти."),
            (chef, "Тесто на пиццу не прошло, плитку не открывать."),
        };
        for (var index = 0; index < notes.Length; index++)
        {
            var (who, text) = notes[index];
            db.StaffMessages.Add(new StaffMessage
            {
                Id = Guid.Parse($"35000000-0000-0000-0000-{index + 1:000000000000}"),
                AuthorId = who.Id,
                Channel = "Staff",
                Text = text,
                CreatedAt = now.AddHours(-index * 3),
            });
        }

        db.StaffMessages.Add(new StaffMessage
        {
            Id = Guid.Parse("35000000-0000-0000-0000-000000000020"),
            Channel = "System",
            Text = "Салат, 420 г: порча. Срок вышел, с готовки снят.",
            CreatedAt = now.AddHours(-3),
        });
        if (supply is not null)
        {
            db.StaffMessages.AddRange(
                new StaffMessage
                {
                    Id = Guid.Parse("35000000-0000-0000-0000-000000000030"),
                    AuthorId = supply.Id,
                    Channel = "Supply",
                    Text = "Рыба будет к обеду, сливки везу отдельно.",
                    CreatedAt = now.AddHours(-4),
                },
                new StaffMessage
                {
                    Id = Guid.Parse("35000000-0000-0000-0000-000000000031"),
                    AuthorId = manager.Id,
                    Channel = "Supply",
                    Text = "Сливки берите с запасом, банкет в субботу.",
                    CreatedAt = now.AddHours(-3),
                });
        }

        (Guid Product, decimal Qty, string Note, PurchaseSource Source, Guid Who)[] requests =
        [
            (MoneyCatalog.Shrimp, 700, "гриль в пятницу", PurchaseSource.Chef, chef.Id),
            (MoneyCatalog.Lemon, 400, "к рыбе", PurchaseSource.Chef, kitchen2.Id),
            (MilkId, 6000, "кофейня на выходные", PurchaseSource.Manager, buyer.Id),
            (MoneyCatalog.Butter, 800, "выпечка", PurchaseSource.Forecast, manager.Id),
            (PastaId, 2500, "запас на неделю", PurchaseSource.Manager, buyer.Id),
            (MoneyCatalog.Garlic, 300, "соус", PurchaseSource.Chef, chef.Id),
        ];
        for (var index = 0; index < requests.Length; index++)
        {
            var row = requests[index];
            db.PurchaseLines.Add(new PurchaseLine
            {
                Id = Guid.Parse($"36000000-0000-0000-0000-{index + 1:000000000000}"),
                ProductId = row.Product,
                Quantity = row.Qty,
                Note = row.Note,
                Source = row.Source,
                RequestedBy = row.Who,
                CreatedAt = now.AddHours(-6 - index),
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        (string Id, UserAccount Who, string Table, Guid Dish, int Portions, int Days, int Hour, Guid Product, decimal Qty)[] sales =
        [
            ("51000000-0000-0000-0000-000000000001", waiter, "4", MoneyCatalog.PastaDishId, 2, 0, 12, SalmonId, 200),
            ("51000000-0000-0000-0000-000000000002", waiter2, "6", MoneyCatalog.PizzaDishId, 1, 0, 13, MozzarellaId, 150),
            ("51000000-0000-0000-0000-000000000003", waiter, "2", MoneyCatalog.CoffeeDishId, 4, 0, 15, MilkId, 400),
            ("51000000-0000-0000-0000-000000000004", waiter2, "9", MoneyCatalog.CapreseDishId, 2, 1, 14, TomatoId, 160),
            ("51000000-0000-0000-0000-000000000005", waiter, "1", MoneyCatalog.CheesecakeDishId, 2, 1, 20, CreamId, 160),
            ("51000000-0000-0000-0000-000000000006", waiter2, "8", MoneyCatalog.PastaDishId, 3, 2, 19, SalmonId, 300),
            ("51000000-0000-0000-0000-000000000007", waiter, "5", Guid.Parse("20000000-0000-0000-0000-000000000005"), 2, 3, 18, MoneyCatalog.Chicken, 240),
            ("51000000-0000-0000-0000-000000000008", waiter2, "12", Guid.Parse("20000000-0000-0000-0000-000000000006"), 2, 4, 13, MoneyCatalog.Beef, 200),
            ("51000000-0000-0000-0000-000000000009", waiter, "3", Guid.Parse("20000000-0000-0000-0000-000000000007"), 1, 5, 21, MoneyCatalog.Shrimp, 120),
            ("51000000-0000-0000-0000-000000000010", waiter2, "10", MoneyCatalog.CoffeeDishId, 5, 6, 11, CoffeeId, 90),
            ("51000000-0000-0000-0000-000000000011", waiter, "7", MoneyCatalog.PizzaDishId, 2, 7, 16, MozzarellaId, 200),
            ("51000000-0000-0000-0000-000000000012", waiter2, "14", MoneyCatalog.CheesecakeDishId, 1, 8, 22, CreamId, 80),
            ("51000000-0000-0000-0000-000000000013", waiter, "6", MoneyCatalog.CapreseDishId, 3, 9, 15, TomatoId, 200),
            ("51000000-0000-0000-0000-000000000014", waiter2, "2", MoneyCatalog.PastaDishId, 1, 11, 18, PastaId, 100),
        ];
        foreach (var sale in sales)
        {
            await SellFilled(sale.Id, sale.Who, sale.Table, sale.Dish, sale.Portions, sale.Days, sale.Hour, sale.Product, sale.Qty);
        }

        await WasteFilled(TomatoId, 180, nameof(WriteOffReason.Waste), MovementType.KitchenWriteOff, 1);
        await WasteFilled(MoneyCatalog.Lettuce, 90, nameof(WriteOffReason.Spoilage), MovementType.KitchenWriteOff, 2);
        await WasteFilled(CreamId, 200, nameof(WriteOffReason.StaffMeal), MovementType.KitchenWriteOff, 3);
        await WasteFilled(MoneyCatalog.Chicken, 220, nameof(WriteOffReason.Training), MovementType.KitchenWriteOff, 5);
        await WasteFilled(MilkId, 300, nameof(WriteOffReason.Waste), MovementType.KitchenWriteOff, 6);
        await db.SaveChangesAsync(cancellationToken);

        async Task SellFilled(string orderId, UserAccount who, string table, Guid dishId, int portions, int daysAgo, int hour, Guid productId, decimal writeOffQty)
        {
            var at = DateTime.SpecifyKind(now.Date.AddDays(-daysAgo).AddHours(hour), DateTimeKind.Utc);
            var orderGuid = Guid.Parse(orderId);
            var batch = await db.Batches.FirstOrDefaultAsync(
                row => row.ProductId == productId && row.QuantityRemaining >= writeOffQty && row.Status == BatchStatus.Active && row.ExpiresAt > now,
                cancellationToken);
            db.Orders.Add(new CustomerOrder
            {
                Id = orderGuid,
                CreatedAt = at,
                CreatedBy = who.Id,
                Status = OrderStatus.Accepted,
                TableNumber = table,
                Items = [new OrderItem { Id = Guid.NewGuid(), OrderId = orderGuid, DishId = dishId, Portions = portions, KitchenStatus = KitchenStatus.Ready }],
            });
            if (batch is null)
            {
                return;
            }

            var before = batch.QuantityRemaining;
            batch.QuantityRemaining -= writeOffQty;
            db.StockMovements.Add(new StockMovement
            {
                Id = Guid.NewGuid(),
                BatchId = batch.Id,
                OrderId = orderGuid,
                Type = MovementType.OrderWriteOff,
                Quantity = writeOffQty,
                QuantityBefore = before,
                CreatedAt = at,
            });
        }

        async Task WasteFilled(Guid productId, decimal quantity, string reason, MovementType type, int daysAgo)
        {
            var batch = await db.Batches.FirstOrDefaultAsync(
                row => row.ProductId == productId && row.QuantityRemaining >= quantity && row.Status == BatchStatus.Active,
                cancellationToken);
            if (batch is null)
            {
                return;
            }

            var before = batch.QuantityRemaining;
            batch.QuantityRemaining -= quantity;
            if (batch.QuantityRemaining == 0)
            {
                batch.Status = BatchStatus.Depleted;
            }

            db.StockMovements.Add(new StockMovement
            {
                Id = Guid.NewGuid(),
                BatchId = batch.Id,
                Type = type,
                Reason = reason,
                Quantity = quantity,
                QuantityBefore = before,
                CreatedAt = now.AddDays(-daysAgo).AddHours(16),
            });
        }

        Batch Parked(Guid deliveryId, Guid productId, decimal quantity, DateTime expiresAt)
        {
            var price = MoneyCatalog.Products.First(item => item.Id == productId).Price;
            return new Batch
            {
                Id = Guid.NewGuid(),
                DeliveryId = deliveryId,
                ProductId = productId,
                QuantityInitial = quantity,
                QuantityRemaining = quantity,
                ExpiresAt = expiresAt,
                Status = BatchStatus.Arrived,
                UnitCost = price,
                ReceivedAt = now.AddHours(-2),
            };
        }
    }

    private static ServiceShift ClosedShift(string id, DateTime opened, DateTime closed, Guid openedBy, Guid closedBy) => new()
    {
        Id = Guid.Parse(id),
        OpenedAt = DateTime.SpecifyKind(opened, DateTimeKind.Utc),
        ClosedAt = DateTime.SpecifyKind(closed, DateTimeKind.Utc),
        OpenedBy = openedBy,
        ClosedBy = closedBy,
    };

    private static ShiftHistory History(Guid dishId, string actor, string text, DateTime at) => new()
    {
        Id = Guid.NewGuid(),
        DishId = dishId,
        ActorLogin = actor,
        Text = text,
        At = at,
    };

    private static UserAccount User(string login, string password, UserRole role) => new()
    {
        Id = Guid.NewGuid(),
        Login = login,
        PasswordHash = PasswordHasher.Hash(password),
        Role = role,
    };

    private static Product Product(Guid id, string name, Unit unit) => new()
    {
        Id = id,
        Name = name,
        Unit = unit,
        IsActive = true,
    };

}
