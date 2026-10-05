using BookingPlatform.Application.Common.Outbox;
using BookingPlatform.Application.Notifications;
using BookingPlatform.Domain.Bookings;
using BookingPlatform.Infrastructure.Notifications;

namespace BookingPlatform.UnitTests.Notifications;

public sealed class NotificationTemplateTests
{
    private static readonly BookingNotificationDetails Booking = new()
    {
        BookingId = Guid.NewGuid(),
        TenantName = "Demo <Group>",
        VenueName = "Bistro & Bar",
        VenuePhone = "+27 21 555 0100",
        VenueEmail = "host@bistro.example",
        VenueAddressLine1 = "1 Long Street",
        VenueCity = "Cape Town",
        TimeZone = "Africa/Johannesburg",
        Reference = "BK-20261006-000042",
        Status = BookingStatus.Confirmed,
        StartsAt = new DateTime(2026, 10, 6, 17, 30, 0, DateTimeKind.Utc),
        PartySize = 4,
        SpecialRequests = "<img src=x onerror=alert(1)>",
        CustomerFirstName = "Thandi",
        CustomerEmail = "thandi@example.test",
    };

    [Fact]
    public void Booking_emails_show_venue_local_time_and_encode_user_text()
    {
        var rendered = EmailTemplates.Booking(Booking, NotificationTypes.BookingConfirmation, "https://book.example/booking/manage#abc");

        rendered.Template.Should().Be(NotificationTemplates.BookingConfirmed);
        rendered.Subject.Should().Be("Your booking at Bistro & Bar is confirmed");
        rendered.Text.Should().Contain("Hi Thandi,").And.Contain("Date: Tuesday 6 October 2026").And.Contain("Time: 19:30",
            "17:30 UTC is 19:30 in Johannesburg").And.Contain("Address: 1 Long Street, Cape Town").And.Contain("https://book.example/booking/manage#abc");
        rendered.Html.Should().Contain("&lt;img src=x onerror=alert(1)&gt;").And.NotContain("<img")
            .And.Contain("Bistro &amp; Bar").And.Contain("Demo &lt;Group&gt;").And.Contain("href=\"https://book.example/booking/manage#abc\"");
    }

    [Fact]
    public void Template_follows_the_message_type_and_status()
    {
        EmailTemplates.Booking(Booking with { Status = BookingStatus.Pending }, NotificationTypes.BookingConfirmation, null).Template
            .Should().Be(NotificationTemplates.BookingRequested);
        var cancelled = EmailTemplates.Booking(Booking with { Status = BookingStatus.Cancelled }, NotificationTypes.BookingCancellation, null);
        cancelled.Template.Should().Be(NotificationTemplates.BookingCancelled);
        cancelled.Text.Should().NotContain("Special requests").And.NotContain("manage");
        EmailTemplates.Booking(Booking with { CustomerFirstName = null }, NotificationTypes.BookingConfirmation, null).Text.Should().StartWith("Hello,");
    }

    [Fact]
    public void Password_reset_email_states_the_expiry()
    {
        var rendered = EmailTemplates.PasswordReset("Ada", "https://book.example/reset-password#tok", TimeSpan.FromMinutes(30));
        rendered.Subject.Should().Be("Reset your password");
        rendered.Text.Should().Contain("expires in 30 minutes").And.Contain("https://book.example/reset-password#tok");
    }

    [Theory]
    [InlineData("https://book.example.com", "Log", true)]
    [InlineData("http://localhost:9000", "Log", true)]
    [InlineData("https://book.example.com/", "Log", false)]
    [InlineData("https://book.example.com/app", "Log", false)]
    [InlineData("ftp://book.example.com", "Log", false)]
    [InlineData("", "Log", false)]
    [InlineData("https://book.example.com", "Smtp", false)]
    [InlineData("https://book.example.com", "Pickup", false)]
    [InlineData("https://book.example.com", "SendGrid", false)]
    public void Options_require_an_origin_and_complete_provider_settings(string baseUrl, string provider, bool valid) =>
        NotificationOptions.IsValid(new NotificationOptions
        {
            PublicBaseUrl = baseUrl,
            Email = new EmailOptions { Provider = provider, FromAddress = "no-reply@example.com" },
        }).Should().Be(valid);

    [Fact]
    public void Options_require_a_valid_sender_and_complete_smtp_settings()
    {
        var options = new NotificationOptions
        {
            PublicBaseUrl = "https://book.example.com",
            Email = new EmailOptions { Provider = EmailProviders.Smtp, FromAddress = "no-reply@example.com", SmtpHost = "smtp.example.com" },
        };
        NotificationOptions.IsValid(options).Should().BeTrue();
        options.Email.FromAddress = "not an address";
        NotificationOptions.IsValid(options).Should().BeFalse();
    }

    [Theory]
    [InlineData("thandi@example.test", "t***@example.test")]
    [InlineData("a@b.c", "a***@b.c")]
    [InlineData("@nolocal", "***")]
    [InlineData("garbage", "***")]
    public void Log_provider_masks_recipients(string address, string masked) => LogEmailProvider.Mask(address).Should().Be(masked);
}
