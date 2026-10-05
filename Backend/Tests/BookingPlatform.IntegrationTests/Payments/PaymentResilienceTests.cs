using System.Net;
using BookingPlatform.Application.Payments;
using BookingPlatform.IntegrationTests.Bookings;
using BookingPlatform.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace BookingPlatform.IntegrationTests.Payments;

/// <summary>A provider that is down: every call fails with an unknown outcome.</summary>
public sealed class UnreachablePaymentProvider : IPaymentProvider
{
    public string Name => "Unreachable";

    public Task<ProviderCheckout> CreateCheckoutAsync(ProviderCheckoutRequest request, CancellationToken cancellationToken) =>
        throw new PaymentProviderException("Connection refused.");

    public Task<ProviderRefundResult> RefundAsync(ProviderRefundRequest request, CancellationToken cancellationToken) =>
        throw new PaymentProviderException("Gateway timeout.");

    public WebhookVerification VerifyWebhook(PaymentWebhookRequest request) => WebhookVerification.Rejected("Not supported.");
}

/// <summary>Provider outages must never corrupt bookings, and an unknown refund outcome must never allow a second refund.</summary>
public sealed class PaymentProviderOutageTests(InfrastructureFixture fixture) : ApiTestBase(fixture)
{
    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?>
    {
        ["Payments:Provider"] = "Unreachable",
        ["Payments:PublicBaseUrl"] = "http://localhost:9000",
    };

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IPaymentProvider, UnreachablePaymentProvider>();

    [Fact]
    public async Task Checkout_and_refund_outages_leave_bookings_and_money_consistent()
    {
        var s = await BookingScenario.CreateAsync(Data, SignInAsync);
        var path = await Data.ScalarAsync<string>(
            "SELECT t.slug || '/' || v.slug FROM venues v JOIN tenants t ON t.id = v.tenant_id WHERE v.id = @VenueId", new { s.VenueId });
        (await s.Session.SendAsync(HttpMethod.Put, $"/api/v1/venues/{s.VenueId}/payment-settings",
                new { depositPolicy = "PER_BOOKING", depositAmountMinor = 10000, currency = "ZAR", paymentWindowMinutes = 30 }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var guest = NewSession();
        var (bookingId, token, confirmation) = await PaymentTests.BookAsync(Data, guest, path, "19:00", 2);
        confirmation.GetProperty("status").GetString().Should().Be("HELD", "the booking is taken before the provider is ever called");
        await (await guest.SendAsync(HttpMethod.Post, "/api/v1/public/booking/payment", headers: new Dictionary<string, string> { ["X-Booking-Token"] = token }))
            .ShouldBeProblemAsync(HttpStatusCode.ServiceUnavailable, "PAYMENT_UNAVAILABLE");
        (await Data.ScalarAsync<string>("SELECT status FROM payments WHERE booking_id = @bookingId", new { bookingId })).Should().Be("CREATED");
        (await Data.ScalarAsync<string>("SELECT status FROM bookings WHERE id = @bookingId", new { bookingId })).Should().Be("HELD");

        // Pretend the guest paid (the provider confirmed before it went down), then try to refund during the outage.
        var paymentId = await Data.ScalarAsync<Guid>("SELECT id FROM payments WHERE booking_id = @bookingId", new { bookingId });
        await Data.ExecuteAsync("UPDATE payments SET status = 'SUCCEEDED', succeeded_at = now() WHERE id = @paymentId", new { paymentId });
        var refundPath = $"/api/v1/bookings/{bookingId}/payments/{paymentId}/refunds";
        var refund = await s.Session.PostIdempotentAsync(refundPath, new { amountMinor = 10000 });
        refund.StatusCode.Should().Be(HttpStatusCode.Created, await refund.Content.ReadAsStringAsync(Ct));
        (await refund.JsonAsync()).GetProperty("status").GetString().Should().Be("PENDING", "the outcome is unknown, so nothing is assumed");
        (await Data.ScalarAsync<bool>("SELECT requires_attention FROM payments WHERE id = @paymentId", new { paymentId })).Should().BeTrue();
        (await Data.ScalarAsync<long>("SELECT refunded_amount_minor FROM payments WHERE id = @paymentId", new { paymentId })).Should().Be(0);
        await (await s.Session.PostIdempotentAsync(refundPath, new { amountMinor = 10000 }))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "REFUND_NOT_ALLOWED");

        var concurrent = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => s.Session.PostIdempotentAsync(refundPath, new { amountMinor = 1 })));
        concurrent.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.UnprocessableEntity, "the pending refund still reserves the full amount");
    }
}

/// <summary>Without a configured provider, deposit venues refuse online bookings cleanly; other venues are unaffected.</summary>
public sealed class PaymentsDisabledTests(InfrastructureFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Deposit_venues_refuse_online_bookings_when_payments_are_off()
    {
        var s = await BookingScenario.CreateAsync(Data, SignInAsync);
        var path = await Data.ScalarAsync<string>(
            "SELECT t.slug || '/' || v.slug FROM venues v JOIN tenants t ON t.id = v.tenant_id WHERE v.id = @VenueId", new { s.VenueId });
        var guest = NewSession();

        var plain = await PaymentTests.BookAsync(Data, guest, path, "18:00", 2);
        plain.Confirmation.GetProperty("status").GetString().Should().Be("CONFIRMED");
        plain.Confirmation.GetProperty("payment").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);

        await (await s.Session.SendAsync(HttpMethod.Put, $"/api/v1/venues/{s.VenueId}/payment-settings",
                new { depositPolicy = "PER_BOOKING", depositAmountMinor = 50, currency = "ZAR", paymentWindowMinutes = 30 }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        var updated = await s.Session.SendAsync(HttpMethod.Put, $"/api/v1/venues/{s.VenueId}/payment-settings",
            new { depositPolicy = "PER_BOOKING", depositAmountMinor = 10000, currency = "ZAR", paymentWindowMinutes = 30 });
        (await updated.JsonAsync()).GetProperty("onlinePaymentsAvailable").GetBoolean().Should().BeFalse();

        var refused = await guest.PostIdempotentAsync($"/api/v1/public/{path}/bookings", new
        {
            date = BookingScenario.Date,
            time = "20:00",
            partySize = 2,
            firstName = "Lerato",
            email = $"guest-{Guid.NewGuid():N}@example.test",
        });
        await refused.ShouldBeProblemAsync(HttpStatusCode.ServiceUnavailable, "PAYMENT_UNAVAILABLE");
        (await Data.ScalarAsync<long>("SELECT count(*) FROM bookings WHERE venue_id = @VenueId", new { s.VenueId })).Should().Be(1);
        (await Data.ScalarAsync<long>("SELECT count(*) FROM audit_logs WHERE action = 'payment.settings.updated' AND entity_id = @VenueId", new { s.VenueId }))
            .Should().Be(1);

        (await s.CreateBookingOkAsync(s.Booking("21:00"))).GetProperty("status").GetString()
            .Should().Be("CONFIRMED", "staff bookings never require a deposit");
    }
}
