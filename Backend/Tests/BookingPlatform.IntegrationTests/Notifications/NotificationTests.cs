using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using BookingPlatform.Application.Notifications;
using BookingPlatform.Infrastructure;
using BookingPlatform.Infrastructure.Notifications;
using BookingPlatform.IntegrationTests.Bookings;
using BookingPlatform.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static BookingPlatform.IntegrationTests.Bookings.BookingScenario;

namespace BookingPlatform.IntegrationTests.Notifications;

/// <summary>
/// Phase 9: the Worker's NOTIFICATION dispatcher wired exactly as in the Worker (real repositories, options and
/// templates) with a capturing email provider, plus the password reset flow end to end through the Api.
/// </summary>
public sealed partial class NotificationTests(InfrastructureFixture fixture) : ApiTestBase(fixture)
{
    private const string BaseUrl = "https://book.example.test";
    private readonly string _tokenKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    private readonly CapturingEmailProvider _email = new();
    private ServiceProvider _worker = null!;

    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?>
    {
        ["PublicBooking:TokenKey"] = _tokenKey,
    };

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = Data.ConnectionString,
            ["ConnectionStrings:Redis"] = Fixture.RedisConnectionString,
            ["PublicBooking:TokenKey"] = _tokenKey,
            ["Notifications:PublicBaseUrl"] = BaseUrl,
            ["Notifications:MaxAttempts"] = "2",
            ["Notifications:Email:FromAddress"] = "no-reply@example.test",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        services.AddPublicBookingTokens(configuration);
        services.AddNotifications(configuration);
        services.AddSingleton<IEmailProvider>(_email);
        services.AddSingleton<NotificationOutboxDispatcher>();
        _worker = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    public override async ValueTask DisposeAsync()
    {
        await _worker.DisposeAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task Guests_get_one_confirmation_with_a_working_manage_link_and_one_cancellation()
    {
        var (s, path) = await SetupAsync();
        await Data.ExecuteAsync("UPDATE venues SET email = 'host@bistro.example', phone = '+27 21 555 0100' WHERE id = @VenueId", new { s.VenueId });
        var guest = NewSession();
        var created = await guest.PostIdempotentAsync($"/api/v1/public/{path}/bookings", Guest("19:00", "grace@example.test"));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var confirmation = await created.JsonAsync();
        var token = confirmation.GetProperty("manageToken").GetString()!;
        var reference = confirmation.GetProperty("reference").GetString()!;

        await DrainAsync();

        var sent = _email.Sent.Should().ContainSingle().Subject;
        sent.To.Should().Be("grace@example.test");
        sent.ToName.Should().Be("Grace");
        sent.ReplyTo.Should().Be("host@bistro.example");
        sent.Subject.Should().Be("Your booking at Bistro is confirmed");
        sent.TextBody.Should().Contain(reference).And.Contain("Guests: 2").And.Contain("Time: 19:00").And.Contain("+27 21 555 0100")
            .And.Contain($"{BaseUrl}/booking/manage#{token}");
        sent.HtmlBody.Should().Contain($"href=\"{BaseUrl}/booking/manage#{token}\"").And.Contain("&lt;script&gt;")
            .And.NotContain("<script>", "user-supplied text is HTML-encoded");

        var manage = await guest.SendAsync(HttpMethod.Get, "/api/v1/public/booking", headers: new Dictionary<string, string> { ["X-Booking-Token"] = token });
        manage.StatusCode.Should().Be(HttpStatusCode.OK, "the emailed link opens the booking");

        var deliveries = await DeliveriesAsync();
        deliveries.Should().ContainSingle().Which.Should().Be(("BookingConfirmed", "SENT", (string?)null));
        (await Data.ScalarAsync<long>("SELECT count(*) FROM outbox_messages WHERE destination = 'NOTIFICATION' AND status <> 'PROCESSED'"))
            .Should().Be(0);

        await Data.ExecuteAsync("UPDATE outbox_messages SET status = 'PENDING', processed_at = NULL, available_at = now() WHERE destination = 'NOTIFICATION'");
        await DrainAsync();
        _email.Sent.Should().HaveCount(1, "a message that already has a delivery record is never sent again");

        (await guest.SendAsync(HttpMethod.Post, "/api/v1/public/booking/cancel", headers: new Dictionary<string, string> { ["X-Booking-Token"] = token }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await DrainAsync();

        var cancelled = _email.Sent.Should().HaveCount(2).And.Subject.Last();
        cancelled.Subject.Should().Be("Your booking at Bistro has been cancelled");
        cancelled.TextBody.Should().Contain("19:00").And.NotContain("/booking/manage", "a cancelled booking has nothing left to manage");
        (await DeliveriesAsync()).Select(d => d.Template).Should().Equal("BookingConfirmed", "BookingCancelled");
    }

    [Fact]
    public async Task Requests_confirmations_and_skips_follow_the_current_booking_state()
    {
        var (s, path) = await SetupAsync();
        await Data.ExecuteAsync("UPDATE venues SET auto_confirm_public_bookings = false WHERE id = @VenueId", new { s.VenueId });
        var pending = await NewSession().PostIdempotentAsync($"/api/v1/public/{path}/bookings", Guest("19:00", "pending@example.test"));
        pending.StatusCode.Should().Be(HttpStatusCode.Created, await pending.Content.ReadAsStringAsync(Ct));
        var pendingId = await Data.ScalarAsync<Guid>("SELECT id FROM bookings WHERE status = 'PENDING'");
        await DrainAsync();
        _email.Sent.Should().ContainSingle().Which.Subject.Should().Be("We received your booking request at Bistro");

        (await s.Session.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{pendingId}/confirm", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
        await DrainAsync();
        _email.Sent.Should().HaveCount(2);
        _email.Sent.Last().Subject.Should().Be("Your booking at Bistro is confirmed");
        _email.Sent.Last().TextBody.Should().Contain("/booking/manage#", "public bookings keep their manage link");

        var staffNoEmail = await s.CreateBookingOkAsync(s.Booking("17:00", 2, Resources(s.T2), customer: new { firstName = "Walk", phone = "+27 82 555 0123" }));
        var staffWithEmail = await s.CreateBookingOkAsync(s.Booking("17:00", 2, Resources(s.T3)));
        var anonymised = await s.CreateBookingOkAsync(s.Booking("21:00", 2, Resources(s.T1),
            customer: new { firstName = "Erased", email = "erased@example.test" }));
        await Data.ExecuteAsync(
            "UPDATE customers SET anonymized_at = now() WHERE id = (SELECT customer_id FROM bookings WHERE id = @id)",
            new { id = anonymised.GetProperty("id").GetGuid() });
        var cancelledBeforeSend = await s.CreateBookingOkAsync(s.Booking("21:00", 2, Resources(s.T2)));
        (await s.Session.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{cancelledBeforeSend.GetProperty("id").GetGuid()}/cancel", new { reason = "Mistake" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await DrainAsync();

        var staffEmail = _email.Sent.Should().HaveCount(4).And.Subject.ElementAt(2);
        staffEmail.Subject.Should().Be("Your booking at Bistro is confirmed");
        staffEmail.TextBody.Should().Contain("Window seat").And.NotContain("/booking/manage", "staff bookings have no guest manage link");
        _email.Sent.Last().Subject.Should().Contain("cancelled", "the cancellation itself is still sent");

        var outcomes = await Data.QueryAsync<(Guid BookingId, string Status, string? Detail)>(
            "SELECT booking_id, status, detail FROM notification_deliveries ORDER BY created_at, id");
        outcomes.Should().Contain((staffNoEmail.GetProperty("id").GetGuid(), "SKIPPED", "Customer has no email address."));
        outcomes.Should().Contain((anonymised.GetProperty("id").GetGuid(), "SKIPPED", "Customer is anonymised."));
        outcomes.Should().Contain((cancelledBeforeSend.GetProperty("id").GetGuid(), "SKIPPED", "Booking is CANCELLED."));
        outcomes.Should().Contain((staffWithEmail.GetProperty("id").GetGuid(), "SENT", null));
        (await Data.ScalarAsync<long>("SELECT count(*) FROM outbox_messages WHERE destination = 'NOTIFICATION' AND status <> 'PROCESSED'"))
            .Should().Be(0, "skips settle the message");
    }

    [Fact]
    public async Task Transport_failures_retry_then_dead_letter_without_touching_the_booking()
    {
        var s = await BookingScenario.CreateAsync(Data, SignInAsync);
        var booking = await s.CreateBookingOkAsync(s.Booking("19:00", 2, Resources(s.T1)));
        var bookingId = booking.GetProperty("id").GetGuid();
        _email.FailWith = new InvalidOperationException("SMTP relay unavailable");

        await DrainAsync();
        (await OutboxAsync()).Should().Be(("FAILED", 1));
        (await Data.ScalarAsync<string>("SELECT last_error FROM outbox_messages WHERE destination = 'NOTIFICATION'"))
            .Should().Be("InvalidOperationException: SMTP relay unavailable");

        await Data.ExecuteAsync("UPDATE outbox_messages SET available_at = now() WHERE destination = 'NOTIFICATION'");
        await DrainAsync();
        (await OutboxAsync()).Should().Be(("DEAD", 2), "MaxAttempts is 2 in this test");
        (await Data.ScalarAsync<string>("SELECT status FROM bookings WHERE id = @bookingId", new { bookingId })).Should().Be("CONFIRMED");
        (await Data.ScalarAsync<long>("SELECT count(*) FROM notification_deliveries")).Should().Be(0, "failed sends leave no delivery record");

        _email.FailWith = new PermanentNotificationException("The recipient was rejected (550).");
        await s.CreateBookingOkAsync(s.Booking("21:00", 2, Resources(s.T1)));
        await DrainAsync();
        (await Data.ScalarAsync<long>("SELECT count(*) FROM outbox_messages WHERE destination = 'NOTIFICATION' AND status = 'DEAD' AND attempt_count = 1"))
            .Should().Be(1, "permanent failures are dead-lettered at once");

        _email.FailWith = null;
        var tenantId = s.TenantId;
        await Data.ExecuteAsync(
            "INSERT INTO outbox_messages (tenant_id, destination, type, payload) VALUES (@tenantId, 'NOTIFICATION', 'Mystery', '{}'::jsonb)", new { tenantId });
        await Data.ExecuteAsync(
            "INSERT INTO outbox_messages (tenant_id, destination, type, payload) VALUES (@tenantId, 'NOTIFICATION', 'BookingConfirmation', '{\"bookingId\": \"not-a-guid\"}'::jsonb)",
            new { tenantId });
        await DrainAsync();
        (await Data.ScalarAsync<long>("SELECT count(*) FROM outbox_messages WHERE destination = 'NOTIFICATION' AND status = 'DEAD'"))
            .Should().Be(4, "unknown types and malformed payloads can never succeed");
        _email.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Password_reset_emails_a_single_use_link_that_signs_out_every_session()
    {
        var user = await Data.CreateUserAsync("reset-me@example.test");
        var (tenantId, _, _) = await Data.CreateTenantWithUserAsync();
        await Data.AddMembershipAsync(tenantId, user.UserId, "TENANT_ADMIN");
        var signedIn = await SignInAsync(user);
        var anonymous = NewSession();

        (await anonymous.SendAsync(HttpMethod.Post, "/api/v1/auth/password/forgot", new { email = "nobody@example.test" }))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await Data.ScalarAsync<long>("SELECT count(*) FROM outbox_messages WHERE type = 'PasswordReset'"))
            .Should().Be(0, "unknown addresses queue nothing but get the same answer");

        (await anonymous.SendAsync(HttpMethod.Post, "/api/v1/auth/password/forgot", new { email = "Reset-Me@Example.test " }))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await Data.ScalarAsync<bool>("SELECT token_hash IS NULL FROM password_reset_requests")).Should().BeTrue("the Api never sees the token");
        await DrainAsync();

        var email = _email.Sent.Should().ContainSingle().Subject;
        email.To.Should().Be("reset-me@example.test");
        email.Subject.Should().Be("Reset your password");
        var token = ResetLink().Match(email.TextBody).Groups[1].Value;
        token.Should().NotBeEmpty();
        email.HtmlBody.Should().Contain($"{BaseUrl}/reset-password#{token}");
        (await Data.QueryAsync<(string Template, string Status, string? Detail)>("SELECT template, status, detail FROM notification_deliveries"))
            .Should().Equal(("PasswordReset", "SENT", null));

        await (await anonymous.SendAsync(HttpMethod.Post, "/api/v1/auth/password/reset", new { token, newPassword = "short" }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await (await anonymous.SendAsync(HttpMethod.Post, "/api/v1/auth/password/reset", new { token = token[..^1] + (token[^1] == 'A' ? 'B' : 'A'), newPassword = "Brand-New-Passphrase-42" }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "INVALID_RESET_TOKEN");

        var reset = await anonymous.SendAsync(HttpMethod.Post, "/api/v1/auth/password/reset", new { token, newPassword = "Brand-New-Passphrase-42" });
        reset.StatusCode.Should().Be(HttpStatusCode.NoContent, await reset.Content.ReadAsStringAsync(Ct));

        await (await signedIn.RefreshAsync(origin: ApiFactory.AllowedOrigin)).ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "INVALID_REFRESH_TOKEN");
        (await NewSession().LoginAsync(user.Email, user.Password)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await NewSession().LoginOkAsync(user.Email, "Brand-New-Passphrase-42");
        await (await anonymous.SendAsync(HttpMethod.Post, "/api/v1/auth/password/reset", new { token, newPassword = "Another-Passphrase-77" }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "INVALID_RESET_TOKEN");
        (await Data.QueryAsync<string>("SELECT action FROM audit_logs WHERE entity_id = @UserId AND action LIKE 'auth.password.reset%' ORDER BY id",
                new { user.UserId }))
            .Should().Equal("auth.password.reset_requested", "auth.password.reset");

        // A newer request supersedes an older one that has not been sent yet; expired links stop working.
        await anonymous.SendAsync(HttpMethod.Post, "/api/v1/auth/password/forgot", new { email = user.Email });
        await anonymous.SendAsync(HttpMethod.Post, "/api/v1/auth/password/forgot", new { email = user.Email });
        await DrainAsync();
        _email.Sent.Should().HaveCount(2, "only the newest request is emailed");
        (await Data.ScalarAsync<string>("SELECT detail FROM notification_deliveries WHERE status = 'SKIPPED'")).Should().Be("Reset request superseded.");

        var latest = ResetLink().Match(_email.Sent.Last().TextBody).Groups[1].Value;
        await Data.ExecuteAsync(
            "UPDATE password_reset_requests SET created_at = now() - interval '2 hours', expires_at = now() - interval '1 minute' WHERE token_hash IS NOT NULL AND used_at IS NULL");
        await (await anonymous.SendAsync(HttpMethod.Post, "/api/v1/auth/password/reset", new { token = latest, newPassword = "Another-Passphrase-77" }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "INVALID_RESET_TOKEN");

        (await anonymous.SendAsync(HttpMethod.Post, "/api/v1/auth/password/forgot", new { email = user.Email }))
            .StatusCode.Should().Be(HttpStatusCode.Accepted, "throttled requests look the same");
        (await Data.ScalarAsync<long>("SELECT count(*) FROM password_reset_requests")).Should().Be(3, "at most three requests per account per hour");
    }

    private async Task DrainAsync() => await _worker.GetRequiredService<NotificationOutboxDispatcher>().DrainAsync(Ct);

    private Task<IReadOnlyList<(string Template, string Status, string? Detail)>> DeliveriesAsync() =>
        Data.QueryAsync<(string, string, string?)>("SELECT template, status, detail FROM notification_deliveries ORDER BY created_at, id");

    private async Task<(string Status, int Attempts)> OutboxAsync() =>
        (await Data.QueryAsync<(string, int)>("SELECT status, attempt_count FROM outbox_messages WHERE destination = 'NOTIFICATION'")).Single();

    private async Task<(BookingScenario Scenario, string Path)> SetupAsync()
    {
        var s = await BookingScenario.CreateAsync(Data, SignInAsync);
        var path = await Data.ScalarAsync<string>(
            "SELECT t.slug || '/' || v.slug FROM venues v JOIN tenants t ON t.id = v.tenant_id WHERE v.id = @VenueId", new { s.VenueId });
        return (s, path);
    }

    private static object Guest(string time, string email) => new
    {
        date = Date,
        time,
        partySize = 2,
        firstName = "Grace",
        lastName = "Hopper",
        email,
        phone = "+27 82 555 0199",
        specialRequests = "<script>alert(1)</script> by the window",
        marketingConsent = false,
    };

    [GeneratedRegex(@"/reset-password#([A-Za-z0-9_-]+)")]
    private static partial Regex ResetLink();

    private sealed class CapturingEmailProvider : IEmailProvider
    {
        private readonly ConcurrentQueue<EmailMessage> _sent = new();

        public string Name => "Test";

        public Exception? FailWith { get; set; }

        public IReadOnlyList<EmailMessage> Sent => _sent.ToList();

        public Task<string?> SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            if (FailWith is { } failure)
            {
                throw failure;
            }

            _sent.Enqueue(message);
            return Task.FromResult<string?>($"<{message.MessageId:N}@test>");
        }
    }
}
