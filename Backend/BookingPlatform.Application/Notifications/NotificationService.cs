using System.Text.Json;
using BookingPlatform.Application.Common.Outbox;
using BookingPlatform.Application.Common.Time;
using BookingPlatform.Application.Identity;
using BookingPlatform.Application.PublicBooking;
using BookingPlatform.Domain.Bookings;
using BookingPlatform.Domain.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BookingPlatform.Application.Notifications;

/// <summary>
/// Turns one NOTIFICATION outbox message into at most one email. Facts are loaded at send time (payloads carry ids
/// only), so a message for a booking that has since changed is sent with current details or deliberately skipped.
/// Every outcome is recorded in <c>notification_deliveries</c>; a message already recorded is never sent twice.
/// </summary>
public sealed class NotificationService(
    INotificationRepository notifications,
    IPasswordResetRepository passwordResets,
    IPublicBookingTokens bookingTokens,
    IEmailProvider emailProvider,
    IOptions<NotificationOptions> options,
    IClock clock,
    ILogger<NotificationService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task HandleAsync(Guid outboxMessageId, Guid? tenantId, string type, string payload, CancellationToken cancellationToken)
    {
        if (await notifications.IsSettledAsync(outboxMessageId, cancellationToken))
        {
            return;
        }

        switch (type)
        {
            case NotificationTypes.BookingConfirmation:
            case NotificationTypes.BookingCancellation:
                await HandleBookingAsync(outboxMessageId, tenantId, type, Deserialize<NotificationPayload>(payload), cancellationToken);
                break;
            case NotificationTypes.PasswordReset:
                await HandlePasswordResetAsync(outboxMessageId, Deserialize<PasswordResetPayload>(payload), cancellationToken);
                break;
            default:
                throw new PermanentNotificationException($"Unknown notification type '{type}'.");
        }
    }

    private async Task HandleBookingAsync(
        Guid outboxMessageId, Guid? tenantId, string type, NotificationPayload payload, CancellationToken cancellationToken)
    {
        if (tenantId is not { } tenant)
        {
            throw new PermanentNotificationException("Booking notifications must belong to a tenant.");
        }

        var booking = await notifications.GetBookingAsync(tenant, payload.BookingId, cancellationToken);
        var skipReason = booking switch
        {
            null => "Booking not found.",
            _ when type == NotificationTypes.BookingCancellation && booking.Status != BookingStatus.Cancelled =>
                $"Booking is {booking.Status}, not cancelled.",
            _ when type == NotificationTypes.BookingConfirmation && booking.Status is not (BookingStatus.Confirmed or BookingStatus.Pending) =>
                $"Booking is {booking.Status}.",
            _ when booking.CustomerAnonymizedAt is not null => "Customer is anonymised.",
            _ when string.IsNullOrWhiteSpace(booking.CustomerEmail) => "Customer has no email address.",
            _ => null,
        };

        if (skipReason is not null)
        {
            await SkipAsync(outboxMessageId, tenant, type, null, payload.BookingId, null, skipReason, cancellationToken);
            return;
        }

        var manageUrl = booking!.HasManageLink && type == NotificationTypes.BookingConfirmation
            ? $"{options.Value.PublicBaseUrl}/booking/manage#{Uri.EscapeDataString(bookingTokens.Create(booking.BookingId))}"
            : null;
        var rendered = EmailTemplates.Booking(booking, type, manageUrl);

        if (await notifications.WasSentAsync(booking.BookingId, rendered.Template, cancellationToken))
        {
            await SkipAsync(outboxMessageId, tenant, type, rendered.Template, booking.BookingId, null, "Already sent for this booking.", cancellationToken);
            return;
        }

        var message = new EmailMessage(
            booking.CustomerEmail!, booking.CustomerFirstName, rendered.Subject, rendered.Text, rendered.Html, booking.VenueEmail, outboxMessageId);
        var providerMessageId = await emailProvider.SendAsync(message, cancellationToken);
        await notifications.RecordAsync(
            new NotificationDelivery(outboxMessageId, tenant, type, rendered.Template, booking.BookingId, null, NotificationDeliveryStatus.Sent,
                emailProvider.Name, providerMessageId, null),
            cancellationToken);
        logger.LogInformation("Sent {Template} email for booking {BookingId} via {Provider}", rendered.Template, booking.BookingId, emailProvider.Name);
    }

    private async Task HandlePasswordResetAsync(Guid outboxMessageId, PasswordResetPayload payload, CancellationToken cancellationToken)
    {
        const string type = NotificationTypes.PasswordReset;
        var now = clock.UtcNow;
        var request = await passwordResets.GetForDeliveryAsync(payload.RequestId, cancellationToken);
        var skipReason = request switch
        {
            null => "Reset request not found.",
            { UsedAt: not null } => "Reset request already used.",
            { InvalidatedAt: not null } => "Reset request superseded.",
            _ when request.ExpiresAt <= now => "Reset request expired before it could be sent.",
            _ when request.UserStatus != UserStatus.Active => "User is not active.",
            _ => null,
        };

        if (skipReason is not null)
        {
            await SkipAsync(outboxMessageId, null, type, NotificationTemplates.PasswordReset, null, request?.UserId, skipReason, cancellationToken);
            return;
        }

        var token = RefreshTokens.Generate();
        if (!await passwordResets.SetTokenHashAsync(request!.RequestId, RefreshTokens.Hash(token), now, cancellationToken))
        {
            await SkipAsync(outboxMessageId, null, type, NotificationTemplates.PasswordReset, null, request.UserId, "Reset request closed before sending.",
                cancellationToken);
            return;
        }

        var remaining = request.ExpiresAt - now;
        var rendered = EmailTemplates.PasswordReset(
            request.DisplayName, $"{options.Value.PublicBaseUrl}/reset-password#{token}", remaining < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : remaining);
        var message = new EmailMessage(request.Email, request.DisplayName, rendered.Subject, rendered.Text, rendered.Html, null, outboxMessageId);
        var providerMessageId = await emailProvider.SendAsync(message, cancellationToken);
        await notifications.RecordAsync(
            new NotificationDelivery(outboxMessageId, null, type, rendered.Template, null, request.UserId, NotificationDeliveryStatus.Sent,
                emailProvider.Name, providerMessageId, null),
            cancellationToken);
        logger.LogInformation("Sent password reset email for user {UserId} via {Provider}", request.UserId, emailProvider.Name);
    }

    private async Task SkipAsync(
        Guid outboxMessageId, Guid? tenantId, string type, string? template, Guid? bookingId, Guid? userId, string reason,
        CancellationToken cancellationToken)
    {
        await notifications.RecordAsync(
            new NotificationDelivery(outboxMessageId, tenantId, type, template, bookingId, userId, NotificationDeliveryStatus.Skipped,
                emailProvider.Name, null, reason),
            cancellationToken);
        logger.LogInformation("Skipped {NotificationType} notification {OutboxMessageId}: {Reason}", type, outboxMessageId, reason);
    }

    private static T Deserialize<T>(string payload)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(payload, JsonOptions) ?? throw new PermanentNotificationException("The notification payload is empty.");
        }
        catch (JsonException ex)
        {
            throw new PermanentNotificationException("The notification payload is malformed.", ex);
        }
    }
}
