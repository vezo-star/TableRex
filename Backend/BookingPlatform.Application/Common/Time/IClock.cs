namespace BookingPlatform.Application.Common.Time;

public interface IClock
{
    /// <summary>Current instant as a UTC <see cref="DateTime"/>.</summary>
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
