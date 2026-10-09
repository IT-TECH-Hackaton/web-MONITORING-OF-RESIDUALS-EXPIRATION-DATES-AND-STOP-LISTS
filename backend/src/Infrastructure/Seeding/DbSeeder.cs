using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Monitoring.Core;
using Monitoring.Core.Time;
using Monitoring.Infrastructure.Auth;
using Monitoring.Infrastructure.Persistence;

namespace Monitoring.Infrastructure.Seeding;

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
        if (!demoData || await db.Products.AnyAsync(cancellationToken))
        {
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
    }

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
