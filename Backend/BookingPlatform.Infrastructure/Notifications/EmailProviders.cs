using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using BookingPlatform.Application.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BookingPlatform.Infrastructure.Notifications;

/// <summary>
/// Sends through an SMTP relay (Mailpit in development, a provider relay in production) or writes .eml files to a
/// pickup directory. A client is created per message because <see cref="SmtpClient"/> is not thread-safe.
/// </summary>
public sealed class SmtpEmailProvider(IOptions<NotificationOptions> options) : IEmailProvider
{
    public string Name => options.Value.Email.Provider;

    public async Task<string?> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var email = options.Value.Email;
        var from = new MailAddress(email.FromAddress, email.FromName);
        var messageId = $"<{message.MessageId:N}@{from.Host}>";

        MailMessage mail;
        try
        {
            mail = new MailMessage
            {
                From = from,
                Subject = message.Subject,
                SubjectEncoding = Encoding.UTF8,
                BodyEncoding = Encoding.UTF8,
                HeadersEncoding = Encoding.UTF8,
                Body = message.TextBody,
                IsBodyHtml = false,
            };
            mail.To.Add(new MailAddress(message.To, message.ToName));
            if (!string.IsNullOrWhiteSpace(message.ReplyTo) && MailAddress.TryCreate(message.ReplyTo, out var replyTo))
            {
                mail.ReplyToList.Add(replyTo);
            }
        }
        catch (FormatException ex)
        {
            throw new PermanentNotificationException("The recipient address is invalid.", ex);
        }

        using (mail)
        {
            mail.Headers.Add("Message-ID", messageId);
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.HtmlBody, Encoding.UTF8, MediaTypeNames.Text.Html));

            using var client = CreateClient(email);
            try
            {
                await client.SendMailAsync(mail, cancellationToken);
            }
            catch (SmtpFailedRecipientException ex) when (ex.StatusCode is SmtpStatusCode.MailboxUnavailable or SmtpStatusCode.MailboxNameNotAllowed
                                                              or SmtpStatusCode.UserNotLocalTryAlternatePath)
            {
                throw new PermanentNotificationException($"The recipient was rejected ({(int)ex.StatusCode}).", ex);
            }
        }

        return messageId;
    }

    private static SmtpClient CreateClient(EmailOptions email)
    {
        if (email.Provider == EmailProviders.Pickup)
        {
            Directory.CreateDirectory(email.PickupDirectory!);
            return new SmtpClient
            {
                DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory,
                PickupDirectoryLocation = Path.GetFullPath(email.PickupDirectory!),
            };
        }

        var client = new SmtpClient(email.SmtpHost, email.SmtpPort)
        {
            DeliveryMethod = SmtpDeliveryMethod.Network,
            EnableSsl = email.SmtpEnableSsl,
            Timeout = 30_000,
        };
        if (!string.IsNullOrEmpty(email.SmtpUsername))
        {
            client.Credentials = new NetworkCredential(email.SmtpUsername, email.SmtpPassword);
        }

        return client;
    }
}

/// <summary>
/// Development fallback that delivers nothing. It logs only a masked recipient and the subject: bodies carry manage and
/// reset tokens, which must never reach logs.
/// </summary>
public sealed class LogEmailProvider(ILogger<LogEmailProvider> logger) : IEmailProvider
{
    public string Name => EmailProviders.Log;

    public Task<string?> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        logger.LogInformation("Email not delivered (Log provider) to {Recipient}: {Subject}", Mask(message.To), message.Subject);
        return Task.FromResult<string?>(null);
    }

    public static string Mask(string address)
    {
        var at = address.IndexOf('@', StringComparison.Ordinal);
        return at <= 0 ? "***" : $"{address[0]}***{address[at..]}";
    }
}
