using System.Globalization;
using System.Net;
using System.Text;
using BookingPlatform.Domain.Bookings;
using NodaTime;

namespace BookingPlatform.Application.Notifications;

/// <summary>
/// Plain-text and HTML bodies for transactional emails. Every value that came from a user is HTML-encoded; links are
/// built only from configuration and server-generated tokens.
/// </summary>
public static class EmailTemplates
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-GB");

    public sealed record Rendered(string Template, string Subject, string Text, string Html);

    public static Rendered Booking(BookingNotificationDetails booking, string type, string? manageUrl)
    {
        ArgumentNullException.ThrowIfNull(booking);
        var (date, time) = LocalStart(booking);
        var greeting = string.IsNullOrWhiteSpace(booking.CustomerFirstName) ? "Hello," : $"Hi {booking.CustomerFirstName},";

        string template, subject, intro;
        if (type == Common.Outbox.NotificationTypes.BookingCancellation)
        {
            template = NotificationTemplates.BookingCancelled;
            subject = $"Your booking at {booking.VenueName} has been cancelled";
            intro = $"Your booking at {booking.VenueName} on {date} at {time} has been cancelled.";
        }
        else if (booking.Status == BookingStatus.Pending)
        {
            template = NotificationTemplates.BookingRequested;
            subject = $"We received your booking request at {booking.VenueName}";
            intro = $"Thanks for your request. {booking.VenueName} will confirm your booking shortly.";
        }
        else
        {
            template = NotificationTemplates.BookingConfirmed;
            subject = $"Your booking at {booking.VenueName} is confirmed";
            intro = $"Your booking at {booking.VenueName} is confirmed. We look forward to seeing you.";
        }

        var details = new List<(string Label, string Value)>
        {
            ("Reference", booking.Reference),
            ("Date", date),
            ("Time", time),
            ("Guests", booking.PartySize.ToString(Culture)),
        };
        var address = string.Join(", ", new[] { booking.VenueAddressLine1, booking.VenueCity }.Where(v => !string.IsNullOrWhiteSpace(v)));
        if (address.Length > 0)
        {
            details.Add(("Address", address));
        }

        if (!string.IsNullOrWhiteSpace(booking.SpecialRequests) && template != NotificationTemplates.BookingCancelled)
        {
            details.Add(("Special requests", booking.SpecialRequests!));
        }

        var contact = string.Join(" / ", new[] { booking.VenuePhone, booking.VenueEmail }.Where(v => !string.IsNullOrWhiteSpace(v)));
        var closing = contact.Length > 0 ? $"Questions? Contact {booking.VenueName}: {contact}." : null;
        var linkLabel = manageUrl is null ? null : "View or cancel your booking";

        return new Rendered(template, subject,
            Text(greeting, intro, details, manageUrl, linkLabel, closing, booking.TenantName),
            Html(subject, greeting, intro, details, manageUrl, linkLabel, closing, booking.TenantName));
    }

    public static Rendered PasswordReset(string displayName, string resetUrl, TimeSpan lifetime)
    {
        var greeting = string.IsNullOrWhiteSpace(displayName) ? "Hello," : $"Hi {displayName},";
        var intro = $"We received a request to reset your Booking Platform password. The link below works once and expires in {lifetime.TotalMinutes:0} minutes.";
        const string closing = "If you didn't ask for this, you can ignore this email; your password stays the same.";
        const string subject = "Reset your password";
        return new Rendered(NotificationTemplates.PasswordReset, subject,
            Text(greeting, intro, [], resetUrl, "Choose a new password", closing, "Booking Platform"),
            Html(subject, greeting, intro, [], resetUrl, "Choose a new password", closing, "Booking Platform"));
    }

    private static (string Date, string Time) LocalStart(BookingNotificationDetails booking)
    {
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(booking.TimeZone) ?? DateTimeZone.Utc;
        var local = Instant.FromDateTimeUtc(DateTime.SpecifyKind(booking.StartsAt, DateTimeKind.Utc)).InZone(zone).LocalDateTime;
        return (local.ToString("dddd d MMMM yyyy", Culture), local.ToString("HH:mm", Culture));
    }

    private static string Text(
        string greeting, string intro, IReadOnlyList<(string Label, string Value)> details, string? url, string? linkLabel, string? closing, string signature)
    {
        var text = new StringBuilder();
        text.AppendLine(greeting).AppendLine().AppendLine(intro).AppendLine();
        foreach (var (label, value) in details)
        {
            text.Append(label).Append(": ").AppendLine(value);
        }

        if (details.Count > 0)
        {
            text.AppendLine();
        }

        if (url is not null)
        {
            text.Append(linkLabel).AppendLine(":").AppendLine(url).AppendLine();
        }

        if (closing is not null)
        {
            text.AppendLine(closing).AppendLine();
        }

        return text.AppendLine(signature).ToString();
    }

    private static string Html(
        string title, string greeting, string intro, IReadOnlyList<(string Label, string Value)> details, string? url, string? linkLabel,
        string? closing, string signature)
    {
        static string E(string value) => WebUtility.HtmlEncode(value);

        var html = new StringBuilder();
        html.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>").Append(E(title)).Append("</title></head>")
            .Append("<body style=\"margin:0;padding:24px;background:#f5f5f5;font-family:Arial,Helvetica,sans-serif;color:#222\">")
            .Append("<div style=\"max-width:560px;margin:0 auto;background:#fff;border-radius:8px;padding:24px\">")
            .Append("<p>").Append(E(greeting)).Append("</p><p>").Append(E(intro)).Append("</p>");

        if (details.Count > 0)
        {
            html.Append("<table style=\"width:100%;border-collapse:collapse;margin:16px 0\">");
            foreach (var (label, value) in details)
            {
                html.Append("<tr><td style=\"padding:6px 0;color:#666\">").Append(E(label))
                    .Append("</td><td style=\"padding:6px 0;text-align:right;font-weight:bold\">").Append(E(value)).Append("</td></tr>");
            }

            html.Append("</table>");
        }

        if (url is not null)
        {
            html.Append("<p style=\"margin:24px 0\"><a href=\"").Append(E(url))
                .Append("\" style=\"background:#1976d2;color:#fff;padding:12px 20px;border-radius:4px;text-decoration:none;display:inline-block\">")
                .Append(E(linkLabel ?? url)).Append("</a></p>");
        }

        if (closing is not null)
        {
            html.Append("<p style=\"color:#666\">").Append(E(closing)).Append("</p>");
        }

        return html.Append("<p style=\"color:#999;font-size:12px\">").Append(E(signature)).Append("</p></div></body></html>").ToString();
    }
}
