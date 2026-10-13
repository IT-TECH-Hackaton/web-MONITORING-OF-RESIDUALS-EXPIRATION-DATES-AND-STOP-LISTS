namespace Monitoring.Core;

/// <summary>Сутки до срока — «истекает». Момент истечения и статус Expired — «просрочено». Пустой остаток — Empty.</summary>
public static class FreshnessRules
{
    public const double WarningHours = 24;

    public static Freshness ForBatch(BatchStatus status, DateTime expiresAt, DateTime utcNow)
    {
        if (status == BatchStatus.Expired || expiresAt <= utcNow)
        {
            return Freshness.Expired;
        }

        var hours = (expiresAt - utcNow).TotalHours;
        return hours <= WarningHours ? Freshness.Expiring : Freshness.Ok;
    }

    public static Freshness ForProduct(decimal available, DateTime? nearestExpiry, bool anyExpired, DateTime utcNow)
    {
        if (available <= 0)
        {
            return anyExpired ? Freshness.Expired : Freshness.Empty;
        }

        if (nearestExpiry is null)
        {
            return Freshness.Ok;
        }

        return ForBatch(BatchStatus.Active, nearestExpiry.Value, utcNow);
    }

    public static double HoursLeft(DateTime expiresAt, DateTime utcNow) =>
        (expiresAt - utcNow).TotalHours;
}
