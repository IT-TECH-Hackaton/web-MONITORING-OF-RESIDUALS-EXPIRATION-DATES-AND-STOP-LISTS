namespace Monitoring.Core.Inventory;

public sealed record HotBatch(
    Guid Id,
    string DeliveryNumber,
    Guid ProductId,
    decimal QuantityRemaining,
    DateTime ExpiresAt,
    BatchStatus Status);

public sealed record HotOffer(Guid DishId, string DishName, double Score, string Reason);

public static class HotOfferRanker
{
    public const double HorizonHours = 72;
    private const double ExclusiveBoost = 1.5;

    public static IReadOnlyList<HotOffer> Rank(
        IReadOnlyList<DishRecipe> dishes,
        IReadOnlyList<HotBatch> batches,
        IReadOnlyDictionary<Guid, decimal> available,
        DateTime utcNow,
        int take = 10)
    {
        var dishesUsing = new Dictionary<Guid, int>();
        var maxPortion = new Dictionary<Guid, decimal>();
        foreach (var dish in dishes)
        {
            foreach (var line in dish.Lines)
            {
                dishesUsing[line.ProductId] = dishesUsing.GetValueOrDefault(line.ProductId) + 1;
                var current = maxPortion.GetValueOrDefault(line.ProductId);
                if (line.QuantityPerPortion > current)
                {
                    maxPortion[line.ProductId] = line.QuantityPerPortion;
                }
            }
        }

        var active = batches
            .Where(batch => batch.Status == BatchStatus.Active
                && batch.QuantityRemaining > 0
                && batch.ExpiresAt > utcNow)
            .ToList();

        var pressure = new Dictionary<Guid, double>();
        var atRiskQty = new Dictionary<Guid, decimal>();
        var earliest = new Dictionary<Guid, HotBatch>();

        foreach (var group in active.GroupBy(batch => batch.ProductId))
        {
            var risky = group
                .Where(batch => (batch.ExpiresAt - utcNow).TotalHours <= HorizonHours)
                .ToList();
            if (risky.Count == 0)
            {
                pressure[group.Key] = 0;
                atRiskQty[group.Key] = 0;
                continue;
            }

            var head = risky
                .OrderBy(batch => batch.ExpiresAt)
                .ThenBy(batch => batch.Id)
                .First();
            var hours = Math.Max(0, (head.ExpiresAt - utcNow).TotalHours);
            var urgency = Math.Pow(1 - (hours / HorizonHours), 2);
            var quantity = risky.Sum(batch => batch.QuantityRemaining);
            pressure[group.Key] = urgency * Math.Log(1 + (double)quantity);
            atRiskQty[group.Key] = quantity;
            earliest[group.Key] = head;
        }

        var offers = new List<HotOffer>();
        foreach (var dish in dishes)
        {
            if (dish.Lines.Count == 0)
            {
                continue;
            }

            var canMake = dish.Lines.All(line =>
                available.GetValueOrDefault(line.ProductId) >= line.QuantityPerPortion);
            if (!canMake)
            {
                continue;
            }

            double score = 0;
            var contributions = new List<(double Score, string Text)>();
            foreach (var line in dish.Lines)
            {
                var productPressure = pressure.GetValueOrDefault(line.ProductId);
                var risk = atRiskQty.GetValueOrDefault(line.ProductId);
                if (productPressure <= 0 || risk <= 0)
                {
                    continue;
                }

                var burn = (double)(Math.Min(line.QuantityPerPortion, risk) / risk);
                var max = maxPortion.GetValueOrDefault(line.ProductId);
                var intensity = max <= 0 ? 0 : (double)(line.QuantityPerPortion / max);
                var contribution = productPressure * burn * intensity;
                var users = dishesUsing.GetValueOrDefault(line.ProductId);
                if (users == 1)
                {
                    contribution *= ExclusiveBoost;
                }

                score += contribution;
                if (!earliest.TryGetValue(line.ProductId, out var batch))
                {
                    continue;
                }

                var hoursLeft = Math.Max(0, (batch.ExpiresAt - utcNow).TotalHours);
                var others = Math.Max(0, users - 1);
                var text =
                    $"{line.ProductName}, партия {batch.DeliveryNumber}, осталось {hoursLeft.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} ч, " +
                    $"в риске {StopListCalculator.FormatQuantity(risk)}; норма блюда {StopListCalculator.FormatQuantity(line.QuantityPerPortion)}. " +
                    $"Ещё блюд с этим продуктом: {others}.";
                contributions.Add((contribution, text));
            }

            if (score < 0.01)
            {
                continue;
            }

            var reason = string.Join(" ", contributions.OrderByDescending(item => item.Score).Take(2).Select(item => item.Text));
            offers.Add(new HotOffer(dish.DishId, dish.DishName, score, reason));
        }

        return offers
            .OrderByDescending(offer => offer.Score)
            .ThenBy(offer => offer.DishName, StringComparer.Ordinal)
            .Take(take)
            .ToList();
    }
}
