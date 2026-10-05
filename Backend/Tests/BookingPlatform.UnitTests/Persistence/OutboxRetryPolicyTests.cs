using BookingPlatform.Infrastructure.Outbox;

namespace BookingPlatform.UnitTests.Persistence;

public sealed class OutboxRetryPolicyTests
{
    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(5, 32)]
    public void Delay_doubles_per_attempt(int attempt, double seconds) =>
        OutboxRetryPolicy.Delay(attempt, 0).Should().Be(TimeSpan.FromSeconds(seconds));

    [Fact]
    public void Delay_applies_bounded_jitter_and_caps_at_fifteen_minutes()
    {
        OutboxRetryPolicy.Delay(3, 0.2).Should().Be(TimeSpan.FromSeconds(9.6));
        OutboxRetryPolicy.Delay(3, -5).Should().Be(TimeSpan.FromSeconds(6.4), "jitter is clamped to ±20 %");
        OutboxRetryPolicy.Delay(30, 0.2).Should().Be(OutboxRetryPolicy.MaxDelay);
        OutboxRetryPolicy.Delay(0, 0).Should().Be(TimeSpan.FromSeconds(2), "attempts below 1 are treated as the first");
    }
}
