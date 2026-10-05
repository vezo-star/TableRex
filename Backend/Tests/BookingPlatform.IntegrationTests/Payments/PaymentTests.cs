using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BookingPlatform.Application.Payments;
using BookingPlatform.IntegrationTests.Bookings;
using BookingPlatform.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using static BookingPlatform.IntegrationTests.Bookings.BookingScenario;

namespace BookingPlatform.IntegrationTests.Payments;

/// <summary>
/// Phase 10: deposits through the provider-neutral payment abstraction, using the Simulated provider with a known
/// webhook secret so tests can deliver (and forge) webhooks exactly like a provider would.
/// </summary>
public sealed class PaymentTests(InfrastructureFixture fixture) : ApiTestBase(fixture)
{
    private const string TokenHeader = "X-Booking-Token";
    private static readonly string WebhookSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?>
    {
        ["Payments:Provider"] = "Simulated",
        ["Payments:PublicBaseUrl"] = "http://localhost:9000",
        ["Payments:Simulated:WebhookSecret"] = WebhookSecret,
        ["RateLimiting:Policies:public-booking:PermitLimit"] = "1000",
    };

    [Fact]
    public async Task Deposit_bookings_are_held_until_the_checkout_succeeds_and_can_then_be_refunded()
    {
        var (s, path) = await SetupAsync(DepositPolicies.PerGuest, 5000);
        var settings = await (await s.Session.GetAsync($"/api/v1/venues/{s.VenueId}/payment-settings")).JsonAsync();
        settings.GetProperty("depositPolicy").GetString().Should().Be("PER_GUEST");
        settings.GetProperty("onlinePaymentsAvailable").GetBoolean().Should().BeTrue();

        var guest = NewSession();
        var venue = await (await guest.GetAsync($"/api/v1/public/{path}")).JsonAsync();
        venue.GetProperty("deposit").GetProperty("amountMinor").GetInt64().Should().Be(5000);
        venue.GetProperty("deposit").GetProperty("policy").GetString().Should().Be("PER_GUEST");

        var (bookingId, token, confirmation) = await BookAsync(guest, path, "19:00", 2);
        confirmation.GetProperty("status").GetString().Should().Be("HELD");
        var payment = confirmation.GetProperty("payment");
        payment.GetProperty("status").GetString().Should().Be("CREATED");
        payment.GetProperty("amountMinor").GetInt64().Should().Be(10000, "R50 per guest for two guests");
        payment.GetProperty("currency").GetString().Should().Be("ZAR");
        payment.GetProperty("canPay").GetBoolean().Should().BeTrue();
        (await Data.ScalarAsync<bool>("SELECT expires_at > now() FROM bookings WHERE id = @bookingId", new { bookingId })).Should().BeTrue();
        (await OutboxTypesAsync(bookingId)).Should().Equal(["REALTIME:BookingCreated"], "nothing is emailed until the deposit is paid");

        var checkout = await guest.SendAsync(HttpMethod.Post, "/api/v1/public/booking/payment", headers: Token(token));
        checkout.StatusCode.Should().Be(HttpStatusCode.OK, await checkout.Content.ReadAsStringAsync(Ct));
        var redirect = (await checkout.JsonAsync()).GetProperty("redirectUrl").GetString()!;
        redirect.Should().StartWith("http://localhost:9000/pay/simulated#");
        var fragment = ParseFragment(redirect);
        var paymentId = Guid.Parse(fragment["p"]);
        fragment["a"].Should().Be("10000");
        fragment["r"].Should().Be("http://localhost:9000/booking/payment-return", "the manage token never travels to the provider");
        (await Data.ScalarAsync<string>("SELECT status || ':' || left(provider_reference, 4) FROM payments WHERE id = @paymentId", new { paymentId }))
            .Should().Be("PENDING:sim_");

        await (await guest.SendAsync(HttpMethod.Post, "/api/v1/payments/simulated/complete", new { paymentId, token = "forged", outcome = "SUCCEEDED" }))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "PAYMENT_NOT_FOUND");
        var completed = await guest.SendAsync(HttpMethod.Post, "/api/v1/payments/simulated/complete", new { paymentId, token = fragment["t"], outcome = "SUCCEEDED" });
        completed.StatusCode.Should().Be(HttpStatusCode.NoContent, await completed.Content.ReadAsStringAsync(Ct));

        (await Data.ScalarAsync<string>("SELECT status FROM bookings WHERE id = @bookingId", new { bookingId })).Should().Be("CONFIRMED");
        (await Data.ScalarAsync<string>("SELECT status FROM payments WHERE id = @paymentId", new { paymentId })).Should().Be("SUCCEEDED");
        (await Data.ScalarAsync<string>(
                "SELECT source || ':' || reason FROM booking_status_history WHERE booking_id = @bookingId AND to_status = 'CONFIRMED'", new { bookingId }))
            .Should().Be("SYSTEM:Deposit paid.");
        (await OutboxTypesAsync(bookingId)).Should().Contain("NOTIFICATION:BookingConfirmation");
        (await Data.QueryAsync<string>("SELECT action FROM audit_logs WHERE entity_id = @paymentId ORDER BY created_at, action", new { paymentId }))
            .Should().Equal("payment.checkout_started", "payment.succeeded");
        (await Data.ScalarAsync<string>("SELECT outcome FROM payment_webhook_events WHERE payment_id = @paymentId", new { paymentId })).Should().Be("APPLIED");

        var view = await (await guest.SendAsync(HttpMethod.Get, "/api/v1/public/booking", headers: Token(token))).JsonAsync();
        view.GetProperty("status").GetString().Should().Be("CONFIRMED");
        view.GetProperty("payment").GetProperty("status").GetString().Should().Be("SUCCEEDED");
        view.GetProperty("payment").GetProperty("canPay").GetBoolean().Should().BeFalse();
        await (await guest.SendAsync(HttpMethod.Post, "/api/v1/public/booking/payment", headers: Token(token)))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "PAYMENT_NOT_PAYABLE");

        var staffPayments = await (await s.Session.GetAsync($"/api/v1/bookings/{bookingId}/payments")).JsonAsync();
        staffPayments.GetArrayLength().Should().Be(1);
        staffPayments[0].GetProperty("refundableAmountMinor").GetInt64().Should().Be(10000);

        var (_, host, _) = await HostOfAsync(s);
        await (await host.PostIdempotentAsync($"/api/v1/bookings/{bookingId}/payments/{paymentId}/refunds", new { amountMinor = 100 }))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");

        var refundPath = $"/api/v1/bookings/{bookingId}/payments/{paymentId}/refunds";
        var key = Guid.NewGuid().ToString();
        var partial = await s.Session.PostIdempotentAsync(refundPath, new { amountMinor = 4000, reason = "Two guests fewer" }, key);
        partial.StatusCode.Should().Be(HttpStatusCode.Created, await partial.Content.ReadAsStringAsync(Ct));
        (await partial.JsonAsync()).GetProperty("status").GetString().Should().Be("SUCCEEDED");
        var replay = await s.Session.PostIdempotentAsync(refundPath, new { amountMinor = 4000, reason = "Two guests fewer" }, key);
        replay.Headers.GetValues("Idempotent-Replayed").Should().Equal("true");
        await (await s.Session.PostIdempotentAsync(refundPath, new { amountMinor = 7000 }))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "REFUND_NOT_ALLOWED");
        var rest = await s.Session.PostIdempotentAsync(refundPath, new { });
        rest.StatusCode.Should().Be(HttpStatusCode.Created, await rest.Content.ReadAsStringAsync(Ct));
        (await rest.JsonAsync()).GetProperty("amountMinor").GetInt64().Should().Be(6000, "no amount refunds what is left");
        await (await s.Session.PostIdempotentAsync(refundPath, new { }))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "REFUND_NOT_ALLOWED");

        (await Data.ScalarAsync<long>("SELECT refunded_amount_minor FROM payments WHERE id = @paymentId", new { paymentId })).Should().Be(10000);
        (await Data.ScalarAsync<long>("SELECT count(*) FROM payment_refunds WHERE payment_id = @paymentId AND status = 'SUCCEEDED'", new { paymentId })).Should().Be(2);
        (await Data.ScalarAsync<long>("SELECT count(*) FROM audit_logs WHERE entity_id = @paymentId AND action = 'payment.refunded'", new { paymentId })).Should().Be(2);
        var after = await (await s.Session.GetAsync($"/api/v1/bookings/{bookingId}/payments")).JsonAsync();
        after[0].GetProperty("refundableAmountMinor").GetInt64().Should().Be(0);
        after[0].GetProperty("refunds").GetArrayLength().Should().Be(2);
        await BookingInvariants.AssertAsync(Data);
    }

    [Fact]
    public async Task Webhooks_must_be_authentic_are_applied_once_and_never_trust_a_wrong_amount()
    {
        var (s, path) = await SetupAsync(DepositPolicies.PerBooking, 20000);
        var (bookingId, token, _) = await BookAsync(NewSession(), path, "19:00", 2);
        var paymentId = await Data.ScalarAsync<Guid>("SELECT id FROM payments WHERE booking_id = @bookingId", new { bookingId });
        var client = NewSession().Client;

        var unsigned = await client.PostAsync("/api/v1/payments/webhooks/Simulated", Json(Event("evt-a", "payment.succeeded", paymentId, 20000)), Ct);
        await unsigned.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "INVALID_WEBHOOK_SIGNATURE");
        var forged = await PostWebhookAsync(client, Event("evt-a", "payment.succeeded", paymentId, 20000), secret: "not-the-secret-not-the-secret-not-the-secret");
        await forged.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "INVALID_WEBHOOK_SIGNATURE");
        var stale = await PostWebhookAsync(client, Event("evt-a", "payment.succeeded", paymentId, 20000), timestamp: DateTimeOffset.UtcNow.AddMinutes(-20));
        await stale.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "INVALID_WEBHOOK_SIGNATURE");
        (await client.PostAsync("/api/v1/payments/webhooks/NoSuchProvider", Json(new { }), Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Data.ScalarAsync<long>("SELECT count(*) FROM payment_webhook_events")).Should().Be(0, "nothing unauthenticated is stored");

        (await PostWebhookAsync(client, Event("evt-short", "payment.succeeded", paymentId, 100))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Data.ScalarAsync<string>("SELECT status || ':' || requires_attention FROM payments WHERE id = @paymentId", new { paymentId }))
            .Should().Be("CREATED:true", "a short payment is flagged, never accepted");
        (await Data.ScalarAsync<string>("SELECT status FROM bookings WHERE id = @bookingId", new { bookingId })).Should().Be("HELD");

        (await PostWebhookAsync(client, Event("evt-fail", "payment.failed", paymentId, 20000, failure: "Card declined"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Data.ScalarAsync<string>("SELECT status || ':' || failure_reason FROM payments WHERE id = @paymentId", new { paymentId }))
            .Should().Be("FAILED:Card declined");
        var view = await (await NewSession().SendAsync(HttpMethod.Get, "/api/v1/public/booking", headers: Token(token))).JsonAsync();
        view.GetProperty("payment").GetProperty("canPay").GetBoolean().Should().BeTrue("a failed payment can be retried while the booking is held");

        // Two different success events race (provider retries with new ids): the booking is confirmed exactly once.
        var results = await Task.WhenAll(
            PostWebhookAsync(client, Event("evt-ok-1", "payment.succeeded", paymentId, 20000)),
            PostWebhookAsync(client, Event("evt-ok-2", "payment.succeeded", paymentId, 20000)));
        results.Select(r => r.StatusCode).Should().AllBeEquivalentTo(HttpStatusCode.OK);
        (await Data.ScalarAsync<long>("SELECT count(*) FROM booking_status_history WHERE booking_id = @bookingId AND to_status = 'CONFIRMED'", new { bookingId }))
            .Should().Be(1);
        (await Data.QueryAsync<string>(
                "SELECT outcome FROM payment_webhook_events WHERE provider_event_id IN ('evt-ok-1', 'evt-ok-2') ORDER BY outcome", new { }))
            .Should().Equal("APPLIED", "IGNORED");

        var redelivered = await PostWebhookAsync(client, Event("evt-ok-1", "payment.succeeded", paymentId, 20000));
        (await redelivered.JsonAsync()).GetProperty("applied").GetInt32().Should().Be(0);
        (await Data.ScalarAsync<long>("SELECT count(*) FROM payment_webhook_events WHERE provider_event_id = 'evt-ok-1'")).Should().Be(1);
        (await Data.ScalarAsync<long>("SELECT count(*) FROM audit_logs WHERE entity_id = @paymentId AND action = 'payment.succeeded'", new { paymentId })).Should().Be(1);

        (await PostWebhookAsync(client, Event("evt-unknown", "payment.succeeded", Guid.NewGuid(), 20000))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Data.ScalarAsync<string>("SELECT outcome FROM payment_webhook_events WHERE provider_event_id = 'evt-unknown'")).Should().Be("UNMATCHED");
        (await Data.ScalarAsync<long>("SELECT count(*) FROM payment_webhook_events WHERE payload_sha256 IS NULL OR octet_length(payload_sha256) <> 32"))
            .Should().Be(0);
    }

    [Fact]
    public async Task Unpaid_deposits_expire_release_the_table_and_late_payments_are_flagged()
    {
        var (s, path) = await SetupAsync(DepositPolicies.PerBooking, 15000);
        var guest = NewSession();
        var (bookingId, token, _) = await BookAsync(guest, path, "19:00", 2);
        var paymentId = await Data.ScalarAsync<Guid>("SELECT id FROM payments WHERE booking_id = @bookingId", new { bookingId });

        (await ExpireDueAsync()).Should().Be(0, "the payment window is still open");
        await Data.ExecuteAsync("UPDATE bookings SET expires_at = now() - interval '1 minute' WHERE id = @bookingId", new { bookingId });
        var view = await (await guest.SendAsync(HttpMethod.Get, "/api/v1/public/booking", headers: Token(token))).JsonAsync();
        view.GetProperty("payment").GetProperty("canPay").GetBoolean().Should().BeFalse("the window has closed even before the sweeper runs");
        await (await guest.SendAsync(HttpMethod.Post, "/api/v1/public/booking/payment", headers: Token(token)))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "PAYMENT_NOT_PAYABLE");

        (await ExpireDueAsync()).Should().Be(1);
        (await Data.ScalarAsync<string>("SELECT status || ':' || cancellation_reason FROM bookings WHERE id = @bookingId", new { bookingId }))
            .Should().Be("CANCELLED:Deposit not paid in time.");
        (await Data.ScalarAsync<string>("SELECT status FROM payments WHERE id = @paymentId", new { paymentId })).Should().Be("EXPIRED");
        (await Data.QueryAsync<string>("SELECT DISTINCT release_reason FROM booking_allocations WHERE booking_id = @bookingId", new { bookingId }))
            .Should().Equal("EXPIRED");
        (await Data.ScalarAsync<string>("SELECT source FROM booking_status_history WHERE booking_id = @bookingId AND to_status = 'CANCELLED'", new { bookingId }))
            .Should().Be("SYSTEM");
        (await OutboxTypesAsync(bookingId)).Should().Contain("NOTIFICATION:BookingCancellation");
        (await ExpireDueAsync()).Should().Be(0);
        await BookingInvariants.AssertAsync(Data);

        var retaken = await BookAsync(NewSession(), path, "19:00", 2);
        retaken.Confirmation.GetProperty("status").GetString().Should().Be("HELD", "the expired booking released its table");

        var late = await PostWebhookAsync(NewSession().Client, Event("evt-late", "payment.succeeded", paymentId, 15000));
        late.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Data.ScalarAsync<string>("SELECT status || ':' || requires_attention FROM payments WHERE id = @paymentId", new { paymentId }))
            .Should().Be("SUCCEEDED:true", "money arrived for a cancelled booking: staff must refund or rebook");
        (await Data.ScalarAsync<string>("SELECT status FROM bookings WHERE id = @bookingId", new { bookingId })).Should().Be("CANCELLED");

        var cancelled = await guest.SendAsync(HttpMethod.Post, "/api/v1/public/booking/cancel", headers: Token(retaken.Token));
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync(Ct));
        (await Data.ScalarAsync<string>("SELECT status FROM payments WHERE booking_id = @Id", new { Id = retaken.BookingId })).Should().Be("CANCELLED");
    }

    private async Task<int> ExpireDueAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var service = ActivatorUtilities.CreateInstance<DepositExpiryService>(scope.ServiceProvider);
        return await service.ExpireDueAsync(100, Ct);
    }

    private async Task<(BookingScenario Scenario, string Path)> SetupAsync(string policy, long amountMinor)
    {
        var s = await BookingScenario.CreateAsync(Data, SignInAsync);
        var path = await Data.ScalarAsync<string>(
            "SELECT t.slug || '/' || v.slug FROM venues v JOIN tenants t ON t.id = v.tenant_id WHERE v.id = @VenueId", new { s.VenueId });
        var updated = await s.Session.SendAsync(HttpMethod.Put, $"/api/v1/venues/{s.VenueId}/payment-settings",
            new { depositPolicy = policy, depositAmountMinor = amountMinor, currency = "zar", paymentWindowMinutes = 15 });
        updated.StatusCode.Should().Be(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync(Ct));
        (await updated.JsonAsync()).GetProperty("currency").GetString().Should().Be("ZAR");
        return (s, path);
    }

    private async Task<(TestUser User, ApiSession Session, Guid TenantId)> HostOfAsync(BookingScenario s)
    {
        var user = await Data.CreateUserAsync();
        await Data.AddMembershipAsync(s.TenantId, user.UserId, "HOST");
        return (user, await SignInAsync(user), s.TenantId);
    }

    private Task<(Guid BookingId, string Token, JsonElement Confirmation)> BookAsync(ApiSession guest, string path, string time, int partySize) =>
        BookAsync(Data, guest, path, time, partySize);

    internal static async Task<(Guid BookingId, string Token, JsonElement Confirmation)> BookAsync(TestData data, ApiSession guest, string path, string time, int partySize)
    {
        var response = await guest.PostIdempotentAsync($"/api/v1/public/{path}/bookings", new
        {
            date = Date,
            time,
            partySize,
            firstName = "Thandi",
            email = $"guest-{Guid.NewGuid():N}@example.test",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        var body = await response.JsonAsync();
        var token = body.GetProperty("manageToken").GetString()!;
        var bookingId = await data.ScalarAsync<Guid>("SELECT id FROM bookings WHERE public_token_hash = @hash",
            new { hash = SHA256.HashData(Encoding.UTF8.GetBytes(token)) });
        return (bookingId, token, body);
    }

    private static object Event(string id, string type, Guid paymentId, long amountMinor, string? failure = null) =>
        new { id, type, paymentId, reference = "sim_test", amountMinor, currency = "ZAR", failureReason = failure };

    private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static async Task<HttpResponseMessage> PostWebhookAsync(HttpClient client, object body, string? secret = null, DateTimeOffset? timestamp = null)
    {
        var raw = JsonSerializer.SerializeToUtf8Bytes(body);
        var ts = (timestamp ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signed = Encoding.UTF8.GetBytes(ts + ".").Concat(raw).ToArray();
        var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret ?? WebhookSecret), signed)).ToLowerInvariant();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/webhooks/Simulated") { Content = new ByteArrayContent(raw) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add("Simulated-Timestamp", ts);
        request.Headers.Add("Simulated-Signature", "v1=" + signature);
        return await client.SendAsync(request, Ct);
    }

    private Task<IReadOnlyList<string>> OutboxTypesAsync(Guid bookingId) =>
        Data.QueryAsync<string>("SELECT destination || ':' || type FROM outbox_messages WHERE payload::text LIKE @id ORDER BY created_at, type",
            new { id = $"%{bookingId}%" });

    private static Dictionary<string, string> Token(string token) => new() { [TokenHeader] = token };

    private static Dictionary<string, string> ParseFragment(string url) =>
        url[(url.IndexOf('#', StringComparison.Ordinal) + 1)..].Split('&')
            .Select(part => part.Split('=', 2))
            .ToDictionary(kv => kv[0], kv => Uri.UnescapeDataString(kv[1]));
}
