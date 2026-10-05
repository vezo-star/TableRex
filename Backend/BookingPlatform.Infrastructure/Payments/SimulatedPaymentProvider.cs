using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BookingPlatform.Application.Common.Time;
using BookingPlatform.Application.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BookingPlatform.Infrastructure.Payments;

public sealed class SimulatedPaymentOptions
{
    public const string SectionName = "Payments:Simulated";

    /// <summary>
    /// HMAC key for simulated webhooks. Optional: when empty a random per-process key is used, which is enough because the
    /// simulated checkout signs and verifies inside the same Api process. Set it when running several Api instances.
    /// </summary>
    public string? WebhookSecret { get; set; }

    public int TimestampToleranceSeconds { get; set; } = 300;
}

/// <summary>
/// A development/test stand-in for a real provider (never allowed in Production, D-084). It behaves like a hosted
/// checkout with signed webhooks: the "checkout" is a page in the web app, and completing it posts a webhook signed
/// with HMAC-SHA256 over <c>{timestamp}.{body}</c> through exactly the same pipeline a real provider would use.
/// </summary>
public sealed class SimulatedPaymentProvider : IPaymentProvider
{
    public const string ProviderName = "Simulated";
    public const string TimestampHeader = "Simulated-Timestamp";
    public const string SignatureHeader = "Simulated-Signature";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly byte[] _secret;
    private readonly int _tolerance;
    // Read per call: PaymentOptions validation resolves the providers, so touching .Value here would deadlock startup.
    private readonly IOptions<PaymentOptions> _paymentOptions;
    private readonly IClock _clock;

    public SimulatedPaymentProvider(IOptions<SimulatedPaymentOptions> options, IOptions<PaymentOptions> paymentOptions, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(paymentOptions);
        var secret = options.Value.WebhookSecret;
        _secret = string.IsNullOrWhiteSpace(secret) ? RandomNumberGenerator.GetBytes(32) : Encoding.UTF8.GetBytes(secret);
        _tolerance = options.Value.TimestampToleranceSeconds;
        _paymentOptions = paymentOptions;
        _clock = clock;
    }

    public string Name => ProviderName;

    public Task<ProviderCheckout> CreateCheckoutAsync(ProviderCheckoutRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reference = "sim_" + Guid.NewGuid().ToString("N");
        var fragment = string.Join('&',
            "p=" + request.PaymentId.ToString("D"),
            "t=" + Uri.EscapeDataString(CheckoutToken(request.PaymentId)),
            "a=" + request.AmountMinor.ToString(CultureInfo.InvariantCulture),
            "c=" + Uri.EscapeDataString(request.Currency),
            "r=" + Uri.EscapeDataString(request.ReturnUrl));
        return Task.FromResult(new ProviderCheckout(reference, $"{_paymentOptions.Value.PublicBaseUrl}/pay/simulated#{fragment}"));
    }

    public Task<ProviderRefundResult> RefundAsync(ProviderRefundRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(new ProviderRefundResult(RefundStatus.Succeeded, "simr_" + request.RefundId.ToString("N"), null));
    }

    public WebhookVerification VerifyWebhook(PaymentWebhookRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Headers.TryGetValue(TimestampHeader, out var timestampText)
            || !long.TryParse(timestampText, NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp)
            || !request.Headers.TryGetValue(SignatureHeader, out var signature))
        {
            return WebhookVerification.Rejected("Missing signature headers.");
        }

        var age = Math.Abs(_clock.UtcNow.Subtract(DateTime.UnixEpoch).TotalSeconds - timestamp);
        if (age > _tolerance)
        {
            return WebhookVerification.Rejected("Timestamp outside the replay window.");
        }

        var expected = Encoding.ASCII.GetBytes("v1=" + Sign(timestamp, request.Body));
        if (!CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(signature.Trim())))
        {
            return WebhookVerification.Rejected("Signature mismatch.");
        }

        SimulatedEvent? body;
        try
        {
            body = JsonSerializer.Deserialize<SimulatedEvent>(request.Body, JsonOptions);
        }
        catch (JsonException)
        {
            return WebhookVerification.Rejected("Malformed payload.");
        }

        if (body is not { Id.Length: > 0 and <= 200, Type.Length: > 0 and <= 60 } || body.PaymentId == Guid.Empty)
        {
            return WebhookVerification.Rejected("Malformed payload.");
        }

        return WebhookVerification.Accepted([
            new PaymentProviderEvent(body.Id, body.Type, body.PaymentId, body.Reference, body.AmountMinor, body.Currency, body.RefundId, body.FailureReason),
        ]);
    }

    /// <summary>The token in the simulated checkout link: proves the page was opened from a checkout this Api started.</summary>
    public string CheckoutToken(Guid paymentId) =>
        Convert.ToBase64String(HMACSHA256.HashData(_secret, Encoding.UTF8.GetBytes("checkout:" + paymentId.ToString("D"))))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public bool IsValidCheckoutToken(Guid paymentId, string? token) =>
        token is { Length: > 0 and <= 100 }
        && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(CheckoutToken(paymentId)), Encoding.ASCII.GetBytes(token));

    /// <summary>Builds a signed webhook exactly as the simulated provider would deliver it.</summary>
    public PaymentWebhookRequest CreateSignedWebhook(SimulatedEvent evt)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(evt, JsonOptions);
        var timestamp = (long)_clock.UtcNow.Subtract(DateTime.UnixEpoch).TotalSeconds;
        return new PaymentWebhookRequest(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [TimestampHeader] = timestamp.ToString(CultureInfo.InvariantCulture),
                [SignatureHeader] = "v1=" + Sign(timestamp, body),
            },
            body);
    }

    private string Sign(long timestamp, byte[] body)
    {
        var prefix = Encoding.UTF8.GetBytes(timestamp.ToString(CultureInfo.InvariantCulture) + ".");
        var message = new byte[prefix.Length + body.Length];
        prefix.CopyTo(message, 0);
        body.CopyTo(message, prefix.Length);
        return Convert.ToHexString(HMACSHA256.HashData(_secret, message)).ToLowerInvariant();
    }
}

public sealed record SimulatedEvent(
    string Id,
    string Type,
    Guid PaymentId,
    string? Reference,
    long? AmountMinor,
    string? Currency,
    Guid? RefundId = null,
    string? FailureReason = null);

public sealed class PaymentProviderRegistry(IEnumerable<IPaymentProvider> registered, IOptions<PaymentOptions> options) : IPaymentProviderRegistry
{
    private readonly Dictionary<string, IPaymentProvider> _providers = registered.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

    public IPaymentProvider? Active => string.IsNullOrWhiteSpace(options.Value.Provider) ? null : Find(options.Value.Provider);

    public IPaymentProvider? Find(string name) => _providers.GetValueOrDefault(name);
}

/// <summary>
/// Fails startup when <c>Payments:Provider</c> names a provider that is not registered. Providers are resolved only when
/// validation runs: resolving them when the options factory is built would be a cycle, since providers take
/// <see cref="IOptions{PaymentOptions}"/>.
/// </summary>
public sealed class PaymentProviderAvailableValidator(IServiceProvider services, string environmentName) : IValidateOptions<PaymentOptions>
{
    public ValidateOptionsResult Validate(string? name, PaymentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Provider))
        {
            return ValidateOptionsResult.Success;
        }

        var available = services.GetServices<IPaymentProvider>()
            .Any(p => string.Equals(p.Name, options.Provider, StringComparison.OrdinalIgnoreCase));
        return available
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"Payments:Provider names a provider that is not available in the {environmentName} environment. Leave it empty to disable online payments.");
    }
}
