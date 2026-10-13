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

    public static readonly Guid BuyerDeliveryId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    public static readonly Guid KitchenDeliveryId = Guid.Parse("30000000-0000-0000-0000-000000000004");
    public static readonly Guid StoreDeliveryId = Guid.Parse("30000000-0000-0000-0000-000000000005");

    public static readonly (Guid Id, string Name, Unit Unit, decimal Price)[] Products =
    [
        (DbSeeder.SalmonId, "Лосось", Unit.G, 1.80m),
        (DbSeeder.MozzarellaId, "Моцарелла", Unit.G, 0.90m),
        (DbSeeder.TomatoId, "Томаты", Unit.G, 0.25m),
        (DbSeeder.BasilId, "Базилик", Unit.G, 1.20m),
        (DbSeeder.PastaId, "Спагетти", Unit.G, 0.15m),
        (DbSeeder.CreamId, "Сливки", Unit.Ml, 0.20m),
        (DbSeeder.CoffeeId, "Кофе", Unit.G, 2.50m),
        (DbSeeder.MilkId, "Молоко", Unit.Ml, 0.09m),
        (Chicken, "Курица", Unit.G, 0.45m),
        (Beef, "Говядина", Unit.G, 0.95m),
        (Onion, "Лук", Unit.G, 0.08m),
        (Potato, "Картофель", Unit.G, 0.06m),
        (Flour, "Мука", Unit.G, 0.07m),
        (Egg, "Яйцо", Unit.Pcs, 18m),
        (Oil, "Масло", Unit.Ml, 0.18m),
        (Sugar, "Сахар", Unit.G, 0.09m),
        (Butter, "Сливочное масло", Unit.G, 0.85m),
        (Shrimp, "Креветки", Unit.G, 1.60m),
        (Lemon, "Лимон", Unit.G, 0.22m),
        (Garlic, "Чеснок", Unit.G, 0.35m),
        (Rice, "Рис", Unit.G, 0.12m),
        (Cucumber, "Огурец", Unit.G, 0.14m),
        (Lettuce, "Салат", Unit.G, 0.28m),
    ];

    public static readonly (Guid Id, string Name, decimal Sale, (Guid ProductId, decimal Quantity)[] Lines)[] Dishes =
    [
        (Guid.Parse("20000000-0000-0000-0000-000000000001"), "Паста с лососем", 890m, [(DbSeeder.SalmonId, 180m), (DbSeeder.PastaId, 100m), (DbSeeder.CreamId, 80m)]),
        (Guid.Parse("20000000-0000-0000-0000-000000000002"), "Пицца Маргарита", 690m, [(DbSeeder.MozzarellaId, 150m), (DbSeeder.TomatoId, 80m), (DbSeeder.BasilId, 5m)]),
        (Guid.Parse("20000000-0000-0000-0000-000000000003"), "Салат Капрезе", 620m, [(DbSeeder.MozzarellaId, 120m), (DbSeeder.TomatoId, 100m), (DbSeeder.BasilId, 8m)]),
        (Guid.Parse("20000000-0000-0000-0000-000000000004"), "Капучино", 280m, [(DbSeeder.CoffeeId, 18m), (DbSeeder.MilkId, 150m)]),
        (Guid.Parse("20000000-0000-0000-0000-000000000005"), "Цезарь", 540m, [(Chicken, 100m), (Lettuce, 40m), (DbSeeder.MozzarellaId, 30m)]),
        (Guid.Parse("20000000-0000-0000-0000-000000000006"), "Борщ", 420m, [(Beef, 80m), (Potato, 80m), (Onion, 30m), (DbSeeder.TomatoId, 40m)]),
        (Guid.Parse("20000000-0000-0000-0000-000000000007"), "Креветки с рисом", 980m, [(Shrimp, 100m), (Rice, 80m), (Lemon, 20m), (Garlic, 5m)]),
        (Guid.Parse("20000000-0000-0000-0000-000000000008"), "Бургер", 650m, [(Beef, 120m), (Onion, 20m), (Lettuce, 15m), (DbSeeder.TomatoId, 30m)]),
        (Guid.Parse("20000000-0000-0000-0000-000000000009"), "Чизкейк", 390m, [(DbSeeder.CreamId, 80m), (Sugar, 40m), (Egg, 1m), (Butter, 30m)]),
    ];
}
