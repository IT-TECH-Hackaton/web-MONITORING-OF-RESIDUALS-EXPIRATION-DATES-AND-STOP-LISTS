namespace Monitoring.Core.Inventory;

public sealed record ForecastBatch(decimal Quantity, DateTime ExpiresAt);

public sealed record WeeklyProjection(
    decimal ConsumedThisWeek,
    double ElapsedDays,
    decimal ProjectedNeed,
    decimal OnHand,
    decimal Shortfall);

/// <summary>
/// Прогноз менеджеру: расход с понедельника растягивает на полные 7 дней и смотрит, хватит ли партий на следующую неделю.
/// Партии, которые истекут раньше дня, в этот день уже не считает.
/// </summary>
public static class WeeklyForecast
{
    public static DateTime WeekStart(DateTime utcNow)
    {
        var date = utcNow.Date;
        var daysFromMonday = ((int)date.DayOfWeek + 6) % 7;
        return DateTime.SpecifyKind(date.AddDays(-daysFromMonday), DateTimeKind.Utc);
    }

    public static WeeklyProjection Project(
        decimal consumedThisWeek,
        DateTime weekStart,
        DateTime utcNow,
        IReadOnlyList<ForecastBatch> batches)
    {
        var elapsed = Math.Max((utcNow - weekStart).TotalDays, 1d / 24d);
        var projected = consumedThisWeek <= 0
            ? 0m
            : decimal.Round(consumedThisWeek / (decimal)elapsed * 7m, 3, MidpointRounding.AwayFromZero);
        var usable = batches.Where(batch => batch.Quantity > 0 && batch.ExpiresAt > utcNow).ToList();
        var onHand = usable.Sum(batch => batch.Quantity);
        var shortfall = decimal.Round(Unmet(projected, utcNow, usable), 3, MidpointRounding.AwayFromZero);
        return new WeeklyProjection(consumedThisWeek, Math.Round(elapsed, 2), projected, onHand, shortfall);
    }

    private static decimal Unmet(decimal projected, DateTime utcNow, IReadOnlyList<ForecastBatch> batches)
    {
        if (projected <= 0)
        {
            return 0;
        }

        var daily = projected / 7m;
        var stock = batches
            .Select(batch => new Slice(batch.Quantity, batch.ExpiresAt))
            .OrderBy(batch => batch.ExpiresAt)
            .ToList();
        decimal unmet = 0;
        for (var day = 0; day < 7; day++)
        {
            var moment = utcNow.AddDays(day);
            var left = daily;
            foreach (var batch in stock)
            {
                if (left <= 0)
                {
                    break;
                }

                if (batch.Quantity <= 0 || batch.ExpiresAt <= moment)
                {
                    continue;
                }

                var take = Math.Min(batch.Quantity, left);
                batch.Quantity -= take;
                left -= take;
            }

            unmet += left;
        }

        return unmet;
    }

    private sealed class Slice(decimal quantity, DateTime expiresAt)
    {
        public decimal Quantity { get; set; } = quantity;
        public DateTime ExpiresAt { get; } = expiresAt;
    }
}
