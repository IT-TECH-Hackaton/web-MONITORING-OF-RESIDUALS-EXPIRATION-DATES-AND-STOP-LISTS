using Monitoring.Core;
using Monitoring.Core.Inventory;

namespace Monitoring.Core.Tests;

public class HotOfferRankerTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Earlier_expiry_ranks_above_a_distant_one()
    {
        var earlyProduct = Guid.NewGuid();
        var lateProduct = Guid.NewGuid();
        var earlyDish = Guid.NewGuid();
        var lateDish = Guid.NewGuid();

        var dishes = new[]
        {
            new DishRecipe(earlyDish, "Раннее", [new RecipeLine(earlyProduct, "Ранний продукт", 100)]),
            new DishRecipe(lateDish, "Позднее", [new RecipeLine(lateProduct, "Поздний продукт", 100)]),
        };
        var batches = new[]
        {
            new HotBatch(Guid.NewGuid(), "П-1", earlyProduct, 400, Now.AddHours(1), BatchStatus.Active),
            new HotBatch(Guid.NewGuid(), "П-2", lateProduct, 400, Now.AddHours(70), BatchStatus.Active),
        };
        var available = new Dictionary<Guid, decimal>
        {
            [earlyProduct] = 400,
            [lateProduct] = 400,
        };

        var ranked = HotOfferRanker.Rank(dishes, batches, available, Now);

        Assert.Equal(earlyDish, ranked[0].DishId);
        Assert.Contains("П-1", ranked[0].Reason);
    }

    [Fact]
    public void Exclusive_ingredient_ranks_above_a_shared_one()
    {
        var exclusive = Guid.NewGuid();
        var shared = Guid.NewGuid();
        var exclusiveDish = Guid.NewGuid();
        var sharedDish = Guid.NewGuid();
        var otherDish = Guid.NewGuid();
        var expiry = Now.AddHours(12);

        var dishes = new[]
        {
            new DishRecipe(exclusiveDish, "Только здесь", [new RecipeLine(exclusive, "Редкий", 100)]),
            new DishRecipe(sharedDish, "Общее", [new RecipeLine(shared, "Общий", 100)]),
            new DishRecipe(otherDish, "Ещё одно", [new RecipeLine(shared, "Общий", 100)]),
        };
        var batches = new[]
        {
            new HotBatch(Guid.NewGuid(), "П-A", exclusive, 300, expiry, BatchStatus.Active),
            new HotBatch(Guid.NewGuid(), "П-B", shared, 300, expiry, BatchStatus.Active),
        };
        var available = new Dictionary<Guid, decimal>
        {
            [exclusive] = 300,
            [shared] = 300,
        };

        var ranked = HotOfferRanker.Rank(dishes, batches, available, Now);
        var exclusiveOffer = ranked.Single(offer => offer.DishId == exclusiveDish);
        var sharedOffer = ranked.Single(offer => offer.DishId == sharedDish);

        Assert.True(exclusiveOffer.Score > sharedOffer.Score);
    }

    [Fact]
    public void Stopped_dish_is_left_out()
    {
        var product = Guid.NewGuid();
        var dishes = new[]
        {
            new DishRecipe(Guid.NewGuid(), "Нет в наличии", [new RecipeLine(product, "Сыр", 200)]),
        };
        var batches = new[]
        {
            new HotBatch(Guid.NewGuid(), "П-1", product, 50, Now.AddHours(2), BatchStatus.Active),
        };

        var ranked = HotOfferRanker.Rank(dishes, batches, new Dictionary<Guid, decimal> { [product] = 50 }, Now);

        Assert.Empty(ranked);
    }
}
