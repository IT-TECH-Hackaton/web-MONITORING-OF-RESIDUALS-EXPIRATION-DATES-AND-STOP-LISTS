using Monitoring.Core;

namespace Monitoring.Infrastructure.Seeding;

internal static class MoneyCatalog
{
    public static readonly Guid Chicken = Guid.Parse("10000000-0000-0000-0000-000000000009");
    public static readonly Guid Beef = Guid.Parse("10000000-0000-0000-0000-000000000010");
    public static readonly Guid Onion = Guid.Parse("10000000-0000-0000-0000-000000000011");
    public static readonly Guid Potato = Guid.Parse("10000000-0000-0000-0000-000000000012");
    public static readonly Guid Flour = Guid.Parse("10000000-0000-0000-0000-000000000013");
    public static readonly Guid Egg = Guid.Parse("10000000-0000-0000-0000-000000000014");
    public static readonly Guid Oil = Guid.Parse("10000000-0000-0000-0000-000000000015");
    public static readonly Guid Sugar = Guid.Parse("10000000-0000-0000-0000-000000000016");
    public static readonly Guid Butter = Guid.Parse("10000000-0000-0000-0000-000000000017");
    public static readonly Guid Shrimp = Guid.Parse("10000000-0000-0000-0000-000000000018");
    public static readonly Guid Lemon = Guid.Parse("10000000-0000-0000-0000-000000000019");
    public static readonly Guid Garlic = Guid.Parse("10000000-0000-0000-0000-000000000020");
    public static readonly Guid Rice = Guid.Parse("10000000-0000-0000-0000-000000000021");
    public static readonly Guid Cucumber = Guid.Parse("10000000-0000-0000-0000-000000000022");
    public static readonly Guid Lettuce = Guid.Parse("10000000-0000-0000-0000-000000000023");

    public static readonly Guid PastaDishId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    public static readonly Guid PizzaDishId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    public static readonly Guid CapreseDishId = Guid.Parse("20000000-0000-0000-0000-000000000003");
    public static readonly Guid CoffeeDishId = Guid.Parse("20000000-0000-0000-0000-000000000004");
    public static readonly Guid CheesecakeDishId = Guid.Parse("20000000-0000-0000-0000-000000000009");

    public static readonly Guid MetroId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    public static readonly Guid FishShortId = Guid.Parse("30000000-0000-0000-0000-000000000002");
    public static readonly Guid MeatId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    public static readonly Guid VegId = Guid.Parse("30000000-0000-0000-0000-000000000004");
    public static readonly Guid ExpiredVegId = Guid.Parse("30000000-0000-0000-0000-000000000008");
    public static readonly Guid DairyId = Guid.Parse("30000000-0000-0000-0000-000000000005");
    public static readonly Guid FishMainId = Guid.Parse("30000000-0000-0000-0000-000000000006");
    public static readonly Guid DairyPrevId = Guid.Parse("30000000-0000-0000-0000-000000000007");

    /// <summary>Закупка за грамм, миллилитр или штуку, как на накладной 2026.</summary>
    public static readonly (Guid Id, string Name, Unit Unit, decimal Price)[] Products =
    [
        (DbSeeder.SalmonId, "Лосось", Unit.G, 2.15m),
        (DbSeeder.MozzarellaId, "Моцарелла", Unit.G, 0.88m),
        (DbSeeder.TomatoId, "Томаты", Unit.G, 0.22m),
        (DbSeeder.BasilId, "Базилик", Unit.G, 3.20m),
        (DbSeeder.PastaId, "Спагетти", Unit.G, 0.14m),
        (DbSeeder.CreamId, "Сливки", Unit.Ml, 0.38m),
        (DbSeeder.CoffeeId, "Кофе", Unit.G, 2.20m),
        (DbSeeder.MilkId, "Молоко", Unit.Ml, 0.09m),
        (Chicken, "Курица", Unit.G, 0.38m),
        (Beef, "Говядина", Unit.G, 1.10m),
        (Onion, "Лук", Unit.G, 0.06m),
        (Potato, "Картофель", Unit.G, 0.05m),
        (Flour, "Мука", Unit.G, 0.06m),
        (Egg, "Яйцо", Unit.Pcs, 14m),
        (Oil, "Масло", Unit.Ml, 0.18m),
        (Sugar, "Сахар", Unit.G, 0.08m),
        (Butter, "Сливочное масло", Unit.G, 0.90m),
        (Shrimp, "Креветки", Unit.G, 1.55m),
        (Lemon, "Лимон", Unit.G, 0.24m),
        (Garlic, "Чеснок", Unit.G, 0.35m),
        (Rice, "Рис", Unit.G, 0.11m),
        (Cucumber, "Огурец", Unit.G, 0.15m),
        (Lettuce, "Салат", Unit.G, 0.30m),
    ];

    public static readonly (Guid Id, string Name, decimal Sale, (Guid ProductId, decimal Quantity)[] Lines)[] Dishes =
    [
        (PastaDishId, "Паста с лососем", 1290m, [(DbSeeder.SalmonId, 180m), (DbSeeder.PastaId, 100m), (DbSeeder.CreamId, 80m)]),
        (PizzaDishId, "Пицца Маргарита", 790m, [(DbSeeder.MozzarellaId, 150m), (DbSeeder.TomatoId, 80m), (DbSeeder.BasilId, 5m)]),
        (CapreseDishId, "Салат Капрезе", 690m, [(DbSeeder.MozzarellaId, 120m), (DbSeeder.TomatoId, 100m), (DbSeeder.BasilId, 8m)]),
        (CoffeeDishId, "Капучино", 320m, [(DbSeeder.CoffeeId, 18m), (DbSeeder.MilkId, 150m)]),
        (Guid.Parse("20000000-0000-0000-0000-000000000005"), "Цезарь", 720m, [(Chicken, 100m), (Lettuce, 40m), (DbSeeder.MozzarellaId, 30m)]),
        (Guid.Parse("20000000-0000-0000-0000-000000000006"), "Борщ", 490m, [(Beef, 80m), (Potato, 80m), (Onion, 30m), (DbSeeder.TomatoId, 40m)]),
        (Guid.Parse("20000000-0000-0000-0000-000000000007"), "Креветки с рисом", 1450m, [(Shrimp, 100m), (Rice, 80m), (Lemon, 20m), (Garlic, 5m)]),
        (Guid.Parse("20000000-0000-0000-0000-000000000008"), "Бургер", 890m, [(Beef, 120m), (Onion, 20m), (Lettuce, 15m), (DbSeeder.TomatoId, 30m)]),
        (CheesecakeDishId, "Чизкейк", 450m, [(DbSeeder.CreamId, 80m), (Sugar, 40m), (Egg, 1m), (Butter, 30m)]),
    ];

    public sealed record InvoiceLine(Guid ProductId, decimal Quantity, int DaysUntilExpiry, double ExtraHours = 0);

    public sealed record Invoice(
        Guid Id,
        string Number,
        string Supplier,
        int DaysAgo,
        int Hour,
        int Minute,
        InvoiceLine[] Lines);

    public static readonly Invoice[] Invoices =
    [
        new(MetroId, "УПД-4412", "Метро Кеш энд Керри", 8, 10, 20,
        [
            new(DbSeeder.PastaId, 10000, 90),
            new(Flour, 25000, 120),
            new(Sugar, 10000, 180),
            new(Oil, 10000, 150),
            new(Rice, 8000, 180),
            new(DbSeeder.CoffeeId, 2000, 180),
            new(Egg, 90, 18),
        ]),
        new(DairyPrevId, "Н-18803", "Молочный двор", 5, 8, 5,
        [
            new(DbSeeder.MilkId, 10000, 6),
            new(DbSeeder.MozzarellaId, 2000, 14),
            new(Butter, 1000, 25),
        ]),
        new(FishMainId, "СФ-9021", "Северная рыба", 3, 7, 40,
        [
            new(DbSeeder.SalmonId, 5000, 6),
            new(Shrimp, 2000, 4),
            new(Lemon, 1500, 10),
        ]),
        new(MeatId, "МД-310", "Мясной двор", 2, 6, 30,
        [
            new(Chicken, 8000, 4),
            new(Beef, 5000, 5),
        ]),
        new(DairyId, "Н-18840", "Молочный двор", 1, 8, 15,
        [
            new(DbSeeder.MilkId, 20000, 5),
            new(DbSeeder.CreamId, 4000, 7),
            new(DbSeeder.MozzarellaId, 3000, 12),
            new(Butter, 2000, 21),
        ]),
        new(FishShortId, "СФ-9044", "Северная рыба", 0, 16, 10,
        [
            new(DbSeeder.MozzarellaId, 400, 0, 5),
            new(DbSeeder.SalmonId, 800, 1, 6),
        ]),
        new(VegId, "ОВ-712", "Овощи Юг", 0, 9, 0,
        [
            new(DbSeeder.TomatoId, 8000, 6),
            new(Onion, 5000, 12),
            new(Potato, 12000, 18),
            new(Cucumber, 4000, 6),
            new(Lettuce, 1500, 4),
            new(Garlic, 400, 20),
            new(DbSeeder.BasilId, 150, 5),
        ]),
        new(ExpiredVegId, "ОВ-698", "Овощи Юг", 3, 10, 40,
        [
            new(Lettuce, 800, 2),
            new(DbSeeder.BasilId, 80, 2),
        ]),
    ];
}
