namespace Monitoring.Core.Time;

public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

public sealed class MutableClock : IClock
{
    public DateTime UtcNow { get; set; } = DateTime.UtcNow;
}
