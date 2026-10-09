using Monitoring.Core.Inventory;

namespace Monitoring.Core.Tests;

public class WeeklyForecastTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Week_starts_on_monday()
    {
        Assert.Equal(new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc), WeeklyForecast.WeekStart(Now));
    }

    [Fact]
    public void Long_lived_stock_covers_the_same_pace()
    {
        var projection = WeeklyForecast.Project(
            70,
            Now.AddDays(-7),
            Now,
            [new ForecastBatch(2000, Now.AddDays(30))]);

        Assert.Equal(70, projection.ProjectedNeed);
        Assert.Equal(0, projection.Shortfall);
    }

    [Fact]
    public void Shortage_is_the_gap_above_on_hand_stock()
    {
        var projection = WeeklyForecast.Project(
            700,
            Now.AddDays(-7),
            Now,
            [new ForecastBatch(200, Now.AddDays(30))]);

        Assert.Equal(500, projection.Shortfall);
        Assert.Equal(200, projection.OnHand);
    }

    [Fact]
    public void Stock_that_expires_before_it_is_used_does_not_cover_the_week()
    {
        var projection = WeeklyForecast.Project(
            700,
            Now.AddDays(-7),
            Now,
            [new ForecastBatch(1000, Now.AddDays(1))]);

        Assert.Equal(700, projection.ProjectedNeed);
        Assert.Equal(1000, projection.OnHand);
        Assert.Equal(600, projection.Shortfall);
    }

    [Fact]
    public void No_consumption_means_no_shortage()
    {
        var projection = WeeklyForecast.Project(
            0,
            WeeklyForecast.WeekStart(Now),
            Now,
            [new ForecastBatch(10, Now.AddHours(2))]);

        Assert.Equal(0, projection.ProjectedNeed);
        Assert.Equal(0, projection.Shortfall);
    }
}
