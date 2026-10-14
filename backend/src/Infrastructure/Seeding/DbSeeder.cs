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
        var delivery = new Delivery { Id = id, Number = number, Supplier = supplier, CreatedAt = at, ReceivedBy = userId, Batches = batches.ToList() };
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

        var now = clock.UtcNow;
        var chef = await db.Users.FirstOrDefaultAsync(user => user.Login == "kitchen", cancellationToken);
        var fresh = !await db.ShiftDishes.AnyAsync(cancellationToken);

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
