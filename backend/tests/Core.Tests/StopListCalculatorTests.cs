using Monitoring.Core.Inventory;

namespace Monitoring.Core.Tests;

public class StopListCalculatorTests
{
    private static readonly Guid Salmon = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1");
    private static readonly Guid Pasta = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2");

    [Fact]
    public void Exact_portion_stays_available()
    {
        var dish = new DishRecipe(
            Guid.NewGuid(),
            "Паста с лососем",
            [
                new RecipeLine(Salmon, "Лосось", 180),
                new RecipeLine(Pasta, "Спагетти", 100),
            ]);

        var entries = StopListCalculator.Calculate(
            [dish],
            new Dictionary<Guid, ProductAvailability>
            {
                [Salmon] = new(180, DateTime.UtcNow.AddHours(5)),
                [Pasta] = new(100, DateTime.UtcNow.AddDays(3)),
            });

        var entry = Assert.Single(entries);
        Assert.False(entry.IsStopped);
        Assert.Equal(1, entry.PortionsPossible);
        Assert.Null(entry.Reason);
    }

    [Fact]
    public void Shortage_puts_dish_on_stop_with_reason()
    {
        var dish = new DishRecipe(
            Guid.NewGuid(),
            "Паста с лососем",
            [new RecipeLine(Salmon, "Лосось", 180)]);

        var entries = StopListCalculator.Calculate(
            [dish],
            new Dictionary<Guid, ProductAvailability>
            {
                [Salmon] = new(50, null),
            });

        var entry = Assert.Single(entries);
        Assert.True(entry.IsStopped);
        Assert.Equal(0, entry.PortionsPossible);
        Assert.Contains("Лосось", entry.Reason);
        Assert.Contains("130", entry.Reason);
    }
}
