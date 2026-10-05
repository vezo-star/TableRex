using System.Net.Mail;

namespace BookingPlatform.Application.Notifications;

public sealed record EmailMessage(
    string To,
    string? ToName,
    string Subject,
    string TextBody,
    string HtmlBody,
    string? ReplyTo,
    Guid MessageId);

/// <summary>Email transport (SMTP relay, pickup directory or log). Throws on failure; the outbox dispatcher retries.</summary>
public interface IEmailProvider
{
    string Name { get; }

    /// <returns>The provider's message id when it reports one.</returns>
    Task<string?> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>A notification that can never be delivered (malformed payload, invalid address); it is dead-lettered at once.</summary>
public sealed class PermanentNotificationException(string message, Exception? innerException = null) : Exception(message, innerException);

public static class EmailProviders
{
    public const string Log = "Log";
    public const string Smtp = "Smtp";
    public const string Pickup = "Pickup";
}

public sealed class EmailOptions
{
    public string Provider { get; set; } = EmailProviders.Log;

    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = "Booking Platform";

    public string? SmtpHost { get; set; }

    public int SmtpPort { get; set; } = 587;

    public bool SmtpEnableSsl { get; set; } = true;

    public string? SmtpUsername { get; set; }

    public string? SmtpPassword { get; set; }

    public string? PickupDirectory { get; set; }
}

public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>Origin of the web app used in links (for example <c>https://book.example.com</c>).</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    public int BatchSize { get; set; } = 20;

    public int PollSeconds { get; set; } = 5;

    public int MaxAttempts { get; set; } = 8;

    public EmailOptions Email { get; set; } = new();

    public static bool IsValid(NotificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var email = options.Email;
        var providerValid = email.Provider switch
        {
            EmailProviders.Log => true,
            EmailProviders.Smtp => !string.IsNullOrWhiteSpace(email.SmtpHost) && email.SmtpPort is > 0 and <= 65535,
            EmailProviders.Pickup => !string.IsNullOrWhiteSpace(email.PickupDirectory),
            _ => false,
        };
        return providerValid
            && MailAddress.TryCreate(email.FromAddress, out _)
            && Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out var baseUrl)
            && (baseUrl.Scheme == Uri.UriSchemeHttps || baseUrl.Scheme == Uri.UriSchemeHttp)
            && baseUrl.AbsolutePath == "/" && !options.PublicBaseUrl.EndsWith('/')
            && options.BatchSize is >= 1 and <= 500
            && options.PollSeconds is >= 1 and <= 300
            && options.MaxAttempts is >= 1 and <= 20;
    }
}

public static class NotificationDeliveryStatus
{
    public const string Sent = "SENT";
    public const string Skipped = "SKIPPED";
}

public static class NotificationTemplates
{
    public const string BookingConfirmed = "BookingConfirmed";
    public const string BookingRequested = "BookingRequested";
    public const string BookingCancelled = "BookingCancelled";
    public const string PasswordReset = "PasswordReset";
}

public sealed record NotificationDelivery(
    Guid OutboxMessageId,
    Guid? TenantId,
    string Type,
    string? Template,
    Guid? BookingId,
    Guid? UserId,
    string Status,
    string Provider,
    string? ProviderMessageId,
    string? Detail);

/// <summary>Current booking facts loaded at send time (outbox payloads carry ids only).</summary>
public sealed record BookingNotificationDetails
{
    public Guid BookingId { get; init; }
    public Guid TenantId { get; init; }
    public string TenantName { get; init; } = string.Empty;
    public string VenueName { get; init; } = string.Empty;
    public string? VenuePhone { get; init; }
    public string? VenueEmail { get; init; }
    public string? VenueAddressLine1 { get; init; }
    public string? VenueCity { get; init; }
    public string TimeZone { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime StartsAt { get; init; }
    public DateTime EndsAt { get; init; }
    public int PartySize { get; init; }
    public string? SpecialRequests { get; init; }
    public string? CustomerFirstName { get; init; }
    public string? CustomerEmail { get; init; }
    public DateTime? CustomerAnonymizedAt { get; init; }
    public bool HasManageLink { get; init; }
}

public sealed record PasswordResetDelivery
{
    public Guid RequestId { get; init; }
    public Guid UserId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string UserStatus { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
    public DateTime? UsedAt { get; init; }
    public DateTime? InvalidatedAt { get; init; }
}

public interface INotificationRepository
{
    Task<bool> IsSettledAsync(Guid outboxMessageId, CancellationToken cancellationToken);

    Task<bool> WasSentAsync(Guid bookingId, string template, CancellationToken cancellationToken);

    Task<BookingNotificationDetails?> GetBookingAsync(Guid tenantId, Guid bookingId, CancellationToken cancellationToken);

    /// <summary>Idempotent: a second record for the same outbox message and channel is ignored.</summary>
    Task RecordAsync(NotificationDelivery delivery, CancellationToken cancellationToken);
}
