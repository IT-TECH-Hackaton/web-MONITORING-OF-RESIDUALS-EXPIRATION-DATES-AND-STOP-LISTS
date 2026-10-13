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
            return;
        }

        var now = clock.UtcNow;
        db.Products.AddRange(
            Product(SalmonId, "Лосось", Unit.G),
            Product(MozzarellaId, "Моцарелла", Unit.G),
            Product(TomatoId, "Томаты", Unit.G),
            Product(BasilId, "Базилик", Unit.G),
            Product(PastaId, "Спагетти", Unit.G),
            Product(CreamId, "Сливки", Unit.Ml),
            Product(CoffeeId, "Кофе", Unit.G),
            Product(MilkId, "Молоко", Unit.Ml));

        var pastaDish = Dish(Guid.Parse("20000000-0000-0000-0000-000000000001"), "Паста с лососем",
            (SalmonId, 180m), (PastaId, 100m), (CreamId, 80m));
        var pizza = Dish(Guid.Parse("20000000-0000-0000-0000-000000000002"), "Пицца Маргарита",
            (MozzarellaId, 150m), (TomatoId, 80m), (BasilId, 5m));
        var caprese = Dish(Guid.Parse("20000000-0000-0000-0000-000000000003"), "Салат Капрезе",
            (MozzarellaId, 120m), (TomatoId, 100m), (BasilId, 8m));
        var cappuccino = Dish(Guid.Parse("20000000-0000-0000-0000-000000000004"), "Капучино",
            (CoffeeId, 18m), (MilkId, 150m));
        db.Dishes.AddRange(pastaDish, pizza, caprese, cappuccino);

        db.Deliveries.Add(Delivery(
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            "П-1001",
            now.AddDays(-1),
            Batch(SalmonId, 2000, now.AddDays(8)),
            Batch(MozzarellaId, 2500, now.AddDays(12)),
            Batch(TomatoId, 3000, now.AddDays(5)),
            Batch(BasilId, 200, now.AddDays(4)),
            Batch(PastaId, 4000, now.AddDays(30)),
            Batch(CreamId, 2000, now.AddDays(6)),
            Batch(CoffeeId, 1000, now.AddDays(60)),
            Batch(MilkId, 5000, now.AddDays(3))));

        db.Deliveries.Add(Delivery(
            Guid.Parse("30000000-0000-0000-0000-000000000002"),
            "П-1002",
            now.AddHours(-6),
            Batch(MozzarellaId, 400, now.AddHours(6)),
            Batch(SalmonId, 500, now.AddHours(30))));

        await db.SaveChangesAsync(cancellationToken);
        await EnsureMoneyAsync(cancellationToken);
        await SeedShiftAsync(cancellationToken);
    }

    private async Task EnsureMoneyAsync(CancellationToken cancellationToken)
    {
        await EnsureUserAsync("waiter2", "waiter2123", UserRole.Waiter, "Пётр", cancellationToken);
        await EnsureUserAsync("kitchen2", "kitchen2123", UserRole.Kitchen, "Олег", cancellationToken);
        await EnsureUserAsync("buyer", "buyer123", UserRole.Manager, "Елена", cancellationToken);
        await EnsureUserAsync("store", "store123", UserRole.Kitchen, "Сергей", cancellationToken);
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
            else if (product.PurchasePrice == 0)
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
            else if (dish.SalePrice == 0)
            {
                dish.SalePrice = item.Sale;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (var item in MoneyCatalog.Products)
        {
            await db.Batches.Where(batch => batch.ProductId == item.Id && batch.UnitCost == 0)
                .ExecuteUpdateAsync(setters => setters.SetProperty(batch => batch.UnitCost, item.Price), cancellationToken);
        }

        var buyer = await db.Users.FirstAsync(user => user.Login == "buyer", cancellationToken);
        var chef = await db.Users.FirstAsync(user => user.Login == "kitchen", cancellationToken);
        var store = await db.Users.FirstAsync(user => user.Login == "store", cancellationToken);
        var manager = await db.Users.FirstAsync(user => user.Login == "manager", cancellationToken);
        await db.Deliveries.Where(delivery => delivery.ReceivedBy == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(delivery => delivery.ReceivedBy, manager.Id), cancellationToken);

        var now = clock.UtcNow;
        if (!await db.Deliveries.AnyAsync(delivery => delivery.Id == MoneyCatalog.BuyerDeliveryId, cancellationToken))
        {
            db.Deliveries.Add(PricedDelivery(MoneyCatalog.BuyerDeliveryId, "П-1108", now.AddDays(-2), buyer.Id,
                Line(MoneyCatalog.Chicken, 4000, now.AddDays(4)),
                Line(MoneyCatalog.Beef, 2500, now.AddDays(5)),
                Line(MoneyCatalog.Shrimp, 900, now.AddDays(2)),
                Line(MoneyCatalog.Rice, 3000, now.AddDays(40)),
                Line(MoneyCatalog.Lettuce, 800, now.AddDays(3)),
                Line(MoneyCatalog.Egg, 90, now.AddDays(14)),
                Line(MoneyCatalog.Butter, 600, now.AddDays(20))));
        }

        if (!await db.Deliveries.AnyAsync(delivery => delivery.Id == MoneyCatalog.KitchenDeliveryId, cancellationToken))
        {
            db.Deliveries.Add(PricedDelivery(MoneyCatalog.KitchenDeliveryId, "П-1114", now.AddHours(-5), chef.Id,
                Line(MoneyCatalog.Onion, 1500, now.AddDays(10)),
                Line(MoneyCatalog.Lemon, 700, now.AddDays(6)),
                Line(MoneyCatalog.Garlic, 300, now.AddDays(20)),
                Line(MoneyCatalog.Potato, 4000, now.AddDays(15))));
        }

        if (!await db.Deliveries.AnyAsync(delivery => delivery.Id == MoneyCatalog.StoreDeliveryId, cancellationToken))
        {
            db.Deliveries.Add(PricedDelivery(MoneyCatalog.StoreDeliveryId, "П-1120", now.AddHours(-8), store.Id,
                Line(MoneyCatalog.Flour, 20000, now.AddDays(60)),
                Line(MoneyCatalog.Oil, 5000, now.AddDays(90)),
                Line(MoneyCatalog.Sugar, 10000, now.AddDays(180)),
                Line(MoneyCatalog.Cucumber, 4000, now.AddDays(5)),
                Line(MoneyCatalog.Butter, 2000, now.AddDays(20))));
        }

        var actors = new[] { manager.Id, buyer.Id, store.Id, chef.Id };
        (Guid Product, decimal Qty)[] samples =
        [
            (SalmonId, 800),
            (CoffeeId, 420),
            (MozzarellaId, 1400),
            (MoneyCatalog.Beef, 1800),
            (MoneyCatalog.Chicken, 2600),
            (MoneyCatalog.Shrimp, 700),
            (MoneyCatalog.Butter, 900),
            (MoneyCatalog.Flour, 9000),
            (MilkId, 7000),
            (MoneyCatalog.Egg, 36),
        ];
        for (var ago = 1; ago <= 29; ago++)
        {
            if (ago % 8 == 0)
            {
                continue;
            }

            var id = Guid.Parse($"30000000-0000-0000-0001-{ago:000000000000}");
            if (await db.Deliveries.AnyAsync(delivery => delivery.Id == id, cancellationToken))
            {
                continue;
            }

            var sample = samples[ago % samples.Length];
            var scale = 0.55m + (ago % 5) * 0.28m;
            db.Deliveries.Add(PricedDelivery(id, $"П-20{ago:00}", now.AddDays(-ago).Date.AddHours(11), actors[ago % actors.Length],
                Line(sample.Product, decimal.Round(sample.Qty * scale, 0), now.AddDays(21))));
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

    private Delivery PricedDelivery(Guid id, string number, DateTime at, Guid userId, params Batch[] batches)
    {
        var delivery = new Delivery { Id = id, Number = number, CreatedAt = at, ReceivedBy = userId, Batches = batches.ToList() };
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
        if (await db.ShiftDishes.AnyAsync(cancellationToken))
        {
            return;
        }

        var pasta = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var pizza = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var caprese = Guid.Parse("20000000-0000-0000-0000-000000000003");
        var coffee = Guid.Parse("20000000-0000-0000-0000-000000000004");
        if (!await db.Dishes.AnyAsync(dish => dish.Id == pasta, cancellationToken))
        {
            return;
        }

        var now = clock.UtcNow;
        var chef = await db.Users.FirstOrDefaultAsync(user => user.Login == "kitchen", cancellationToken);
        db.ShiftDishes.AddRange(
            new ShiftDish { DishId = pasta, PrepPortions = 3, IsPriority = true },
            new ShiftDish { DishId = caprese, PrepPortions = 4, IsPriority = true },
            new ShiftDish
            {
                DishId = pizza,
                PrepPortions = 0,
                IsStopped = true,
                StopReason = nameof(StopReason.Quality),
                StopNote = "Тесто не прошло",
                StoppedAt = now.AddHours(-5),
                StoppedBy = chef?.Id,
            },
            new ShiftDish
            {
                DishId = coffee,
                PrepPortions = 0,
                IsStopped = true,
                StopReason = nameof(StopReason.Equipment),
                StopNote = "Кофемашина",
                StoppedAt = now.AddHours(-2),
                StoppedBy = chef?.Id,
            });
        db.ShiftHistory.AddRange(
            History(pizza, "шеф", "Стоп: качество — тесто не прошло", now.AddHours(-5)),
            History(coffee, "су", "Стоп: техника — кофемашина", now.AddHours(-2)),
            History(pasta, "шеф", "В приоритет, заготовки 3", now.AddHours(-1)),
            History(caprese, "шеф", "В приоритет, заготовки 4", now.AddHours(-1)));
        await db.SaveChangesAsync(cancellationToken);
    }

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

    private static Dish Dish(Guid id, string name, params (Guid ProductId, decimal Quantity)[] lines) => new()
    {
        Id = id,
        Name = name,
        IsActive = true,
        Ingredients = lines.Select(line => new DishIngredient
        {
            DishId = id,
            ProductId = line.ProductId,
            Quantity = line.Quantity,
        }).ToList(),
    };

    private static Delivery Delivery(Guid id, string number, DateTime createdAt, params Batch[] batches)
    {
        var delivery = new Delivery { Id = id, Number = number, CreatedAt = createdAt, Batches = batches.ToList() };
        foreach (var batch in batches)
        {
            batch.DeliveryId = id;
            batch.ReceivedAt = createdAt;
            batch.Movements.Add(new StockMovement
            {
                Id = Guid.NewGuid(),
                Type = MovementType.Receipt,
                Quantity = batch.QuantityInitial,
                CreatedAt = createdAt,
            });
        }

        return delivery;
    }

    private static Batch Batch(Guid productId, decimal quantity, DateTime expiresAt) => new()
    {
        Id = Guid.NewGuid(),
        ProductId = productId,
        QuantityInitial = quantity,
        QuantityRemaining = quantity,
        ExpiresAt = expiresAt,
        Status = BatchStatus.Active,
    };
}
