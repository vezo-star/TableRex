using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BookingPlatform.Application.Common.Time;
using BookingPlatform.Application.Payments;
using BookingPlatform.Domain.Bookings;
using BookingPlatform.Infrastructure.Payments;
using Microsoft.Extensions.Options;

namespace BookingPlatform.UnitTests.Payments;

public sealed class PaymentPrimitivesTests
{
    private const string Secret = "unit-test-webhook-secret-of-at-least-32-bytes";

    private sealed class FixedClock(DateTime now) : IClock
    {
        public DateTime UtcNow { get; set; } = now;
    }

    private static readonly DateTime Now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    private static SimulatedPaymentProvider Provider(FixedClock clock, string? secret = Secret) => new(
        Options.Create(new SimulatedPaymentOptions { WebhookSecret = secret }),
        Options.Create(new PaymentOptions { Provider = "Simulated", PublicBaseUrl = "https://book.example.test" }),
        clock);

    private static SimulatedEvent Succeeded(Guid paymentId) => new("evt_1", PaymentEventTypes.PaymentSucceeded, paymentId, "sim_1", 5000, "ZAR");

    [Theory]
    [InlineData("NONE", 5000L, 4, null)]
    [InlineData("PER_BOOKING", 5000L, 4, 5000L)]
    [InlineData("PER_GUEST", 5000L, 4, 20000L)]
    public void Deposits_follow_the_venue_policy(string policy, long amount, int partySize, long? expected) =>
        new VenuePaymentSettings(policy, amount, "ZAR", 30).DepositFor(partySize).Should().Be(expected);

    [Theory]
    [InlineData(null, "", true)]
    [InlineData("", "", true)]
    [InlineData("Simulated", "https://book.example.test", true)]
    [InlineData("Simulated", "", false)]
    [InlineData("Simulated", "https://book.example.test/", false)]
    [InlineData("Simulated", "https://book.example.test/app", false)]
    [InlineData("Bad Name!", "https://book.example.test", false)]
    public void Payment_options_require_a_valid_provider_name_and_origin(string? provider, string baseUrl, bool valid) =>
        PaymentOptions.IsValid(new PaymentOptions { Provider = provider, PublicBaseUrl = baseUrl }).Should().Be(valid);

    [Fact]
    public void Signed_webhooks_verify_and_translate_into_neutral_events()
    {
        var provider = Provider(new FixedClock(Now));
        var paymentId = Guid.NewGuid();

        var result = provider.VerifyWebhook(provider.CreateSignedWebhook(Succeeded(paymentId)));

        result.IsAuthentic.Should().BeTrue(result.RejectionReason);
        result.Events.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new PaymentProviderEvent("evt_1", PaymentEventTypes.PaymentSucceeded, paymentId, "sim_1", 5000, "ZAR"));
    }

    [Fact]
    public void Tampered_unsigned_foreign_and_replayed_webhooks_are_rejected()
    {
        var clock = new FixedClock(Now);
        var provider = Provider(clock);
        var request = provider.CreateSignedWebhook(Succeeded(Guid.NewGuid()));

        var tampered = request with { Body = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(request.Body).Replace("5000", "5", StringComparison.Ordinal)) };
        provider.VerifyWebhook(tampered).IsAuthentic.Should().BeFalse();

        provider.VerifyWebhook(request with { Headers = new Dictionary<string, string>() }).IsAuthentic.Should().BeFalse();

        var foreign = Provider(clock, "a-different-secret-that-is-also-32-bytes-long");
        foreign.VerifyWebhook(request).IsAuthentic.Should().BeFalse("another key never verifies");

        clock.UtcNow = Now.AddMinutes(6);
        provider.VerifyWebhook(request).RejectionReason.Should().Be("Timestamp outside the replay window.");
        clock.UtcNow = Now.AddMinutes(4);
        provider.VerifyWebhook(request).IsAuthentic.Should().BeTrue();
    }

    [Fact]
    public void Authentic_but_malformed_payloads_are_rejected()
    {
        var provider = Provider(new FixedClock(Now));
        var timestamp = ((long)(Now - DateTime.UnixEpoch).TotalSeconds).ToString(CultureInfo.InvariantCulture);
        foreach (var body in new[] { "not json", "{}", "{\"id\":\"e\",\"type\":\"payment.succeeded\",\"paymentId\":\"00000000-0000-0000-0000-000000000000\"}" })
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(timestamp + "." + body))).ToLowerInvariant();
            var request = new PaymentWebhookRequest(
                new Dictionary<string, string> { [SimulatedPaymentProvider.TimestampHeader] = timestamp, [SimulatedPaymentProvider.SignatureHeader] = "v1=" + signature },
                bytes);
            provider.VerifyWebhook(request).RejectionReason.Should().Be("Malformed payload.", body);
        }
    }

    [Fact]
    public async Task Checkout_links_carry_a_token_bound_to_the_payment()
    {
        var provider = Provider(new FixedClock(Now));
        var paymentId = Guid.NewGuid();

        var checkout = await provider.CreateCheckoutAsync(
            new ProviderCheckoutRequest(paymentId, 5000, "ZAR", "Deposit", "https://book.example.test/booking/payment-return", Now.AddMinutes(30)),
            TestContext.Current.CancellationToken);

        checkout.ProviderReference.Should().StartWith("sim_");
        checkout.RedirectUrl.Should().StartWith("https://book.example.test/pay/simulated#p=" + paymentId);
        var token = provider.CheckoutToken(paymentId);
        checkout.RedirectUrl.Should().Contain("t=" + Uri.EscapeDataString(token));
        provider.IsValidCheckoutToken(paymentId, token).Should().BeTrue();
        provider.IsValidCheckoutToken(Guid.NewGuid(), token).Should().BeFalse();
        provider.IsValidCheckoutToken(paymentId, null).Should().BeFalse();
        Provider(new FixedClock(Now), secret: null).IsValidCheckoutToken(paymentId, token).Should().BeFalse("an unset secret is random per process");
    }

    [Fact]
    public void Only_held_bookings_expire()
    {
        BookingStatusMachine.TryApply(BookingStatus.Held, BookingCommand.Expire).Should().Be(BookingStatus.Cancelled);
        foreach (var status in BookingStatus.All.Where(s => s != BookingStatus.Held))
        {
            BookingStatusMachine.CanApply(status, BookingCommand.Expire).Should().BeFalse(status);
        }
    }
}
