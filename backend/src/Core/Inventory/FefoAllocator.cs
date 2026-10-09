namespace Monitoring.Core.Inventory;

public sealed record BatchSlice(
    Guid Id,
    Guid ProductId,
    decimal QuantityRemaining,
    DateTime ExpiresAt,
    DateTime ReceivedAt,
    BatchStatus Status);

public sealed record ProductRequirement(Guid ProductId, decimal Quantity);

public sealed record Shortage(Guid ProductId, decimal Required, decimal Available)
{
    public decimal Deficit => Required - Available;
}

public sealed record Allocation(Guid BatchId, Guid ProductId, decimal Quantity);

public sealed record FefoOutcome(
    bool Success,
    IReadOnlyList<Allocation> Lines,
    IReadOnlyList<Shortage> Shortages)
{
    public static FefoOutcome Ok(IReadOnlyList<Allocation> lines) => new(true, lines, []);

    public static FefoOutcome Failed(IReadOnlyList<Shortage> shortages) => new(false, [], shortages);
}

public static class FefoAllocator
{
    public static FefoOutcome TryAllocate(
        IReadOnlyList<ProductRequirement> requirements,
        IReadOnlyList<BatchSlice> batches,
        DateTime utcNow)
    {
        var ordered = requirements
            .GroupBy(requirement => requirement.ProductId)
            .Select(group => new ProductRequirement(group.Key, group.Sum(item => item.Quantity)))
            .Where(requirement => requirement.Quantity > 0)
            .OrderBy(requirement => requirement.ProductId)
            .ToList();

        var available = batches
            .Where(batch => batch.Status == BatchStatus.Active
                && batch.QuantityRemaining > 0
                && batch.ExpiresAt > utcNow)
            .GroupBy(batch => batch.ProductId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(batch => batch.ExpiresAt)
                    .ThenBy(batch => batch.ReceivedAt)
                    .ThenBy(batch => batch.Id)
                    .ToList());

        var shortages = new List<Shortage>();
        foreach (var requirement in ordered)
        {
            var have = available.TryGetValue(requirement.ProductId, out var slices)
                ? slices.Sum(slice => slice.QuantityRemaining)
                : 0m;
            if (have < requirement.Quantity)
            {
                shortages.Add(new Shortage(requirement.ProductId, requirement.Quantity, have));
            }
        }

        if (shortages.Count > 0)
        {
            return FefoOutcome.Failed(shortages);
        }

        var lines = new List<Allocation>();
        foreach (var requirement in ordered)
        {
            var left = requirement.Quantity;
            foreach (var slice in available[requirement.ProductId])
            {
                if (left <= 0)
                {
                    break;
                }

                var take = Math.Min(slice.QuantityRemaining, left);
                if (take <= 0)
                {
                    continue;
                }

                lines.Add(new Allocation(slice.Id, requirement.ProductId, take));
                left -= take;
            }
        }

        return FefoOutcome.Ok(lines);
    }
}
