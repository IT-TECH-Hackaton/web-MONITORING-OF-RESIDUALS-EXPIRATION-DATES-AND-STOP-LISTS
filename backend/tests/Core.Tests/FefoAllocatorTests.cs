using Monitoring.Core;
using Monitoring.Core.Inventory;

namespace Monitoring.Core.Tests;

public class FefoAllocatorTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Salmon = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
    private static readonly Guid Cream = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2");

    [Fact]
    public void Consumes_earlier_batch_before_later_one()
    {
        var early = Batch(Salmon, 80, Now.AddHours(5), Now.AddDays(-2));
        var later = Batch(Salmon, 100, Now.AddDays(4), Now.AddDays(-1));

        var outcome = FefoAllocator.TryAllocate(
            [new ProductRequirement(Salmon, 90)],
            [later, early],
            Now);

        Assert.True(outcome.Success);
        Assert.Equal(2, outcome.Lines.Count);
        Assert.Equal(early.Id, outcome.Lines[0].BatchId);
        Assert.Equal(80, outcome.Lines[0].Quantity);
        Assert.Equal(later.Id, outcome.Lines[1].BatchId);
        Assert.Equal(10, outcome.Lines[1].Quantity);
    }

    [Fact]
    public void Ignores_expired_and_depleted_batches()
    {
        var expired = new BatchSlice(Guid.NewGuid(), Salmon, 500, Now.AddHours(-1), Now.AddDays(-3), BatchStatus.Active);
        var depleted = new BatchSlice(Guid.NewGuid(), Salmon, 0, Now.AddDays(2), Now.AddDays(-1), BatchStatus.Depleted);
        var usable = Batch(Salmon, 40, Now.AddDays(1), Now);

        var outcome = FefoAllocator.TryAllocate(
            [new ProductRequirement(Salmon, 40)],
            [expired, depleted, usable],
            Now);

        Assert.True(outcome.Success);
        Assert.Single(outcome.Lines);
        Assert.Equal(usable.Id, outcome.Lines[0].BatchId);
    }

    [Fact]
    public void Rejects_when_one_of_several_products_is_short()
    {
        var outcome = FefoAllocator.TryAllocate(
            [
                new ProductRequirement(Salmon, 100),
                new ProductRequirement(Cream, 50),
            ],
            [
                Batch(Salmon, 100, Now.AddDays(1), Now),
                Batch(Cream, 20, Now.AddDays(1), Now),
            ],
            Now);

        Assert.False(outcome.Success);
        Assert.Empty(outcome.Lines);
        var shortage = Assert.Single(outcome.Shortages);
        Assert.Equal(Cream, shortage.ProductId);
        Assert.Equal(50, shortage.Required);
        Assert.Equal(20, shortage.Available);
        Assert.Equal(30, shortage.Deficit);
    }

    [Fact]
    public void Succeeds_when_stock_matches_the_request_exactly()
    {
        var only = Batch(Salmon, 180, Now.AddHours(6), Now);
        var outcome = FefoAllocator.TryAllocate(
            [new ProductRequirement(Salmon, 180)],
            [only],
            Now);

        Assert.True(outcome.Success);
        Assert.Equal(180, Assert.Single(outcome.Lines).Quantity);
    }

    private static BatchSlice Batch(Guid productId, decimal quantity, DateTime expiresAt, DateTime receivedAt) =>
        new(Guid.NewGuid(), productId, quantity, expiresAt, receivedAt, BatchStatus.Active);
}
