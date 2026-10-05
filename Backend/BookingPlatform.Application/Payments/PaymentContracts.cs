using System.Text.RegularExpressions;

namespace BookingPlatform.Application.Payments;

public static class PaymentStatus
{
    public const string Created = "CREATED";
    public const string Pending = "PENDING";
    public const string Succeeded = "SUCCEEDED";
    public const string Failed = "FAILED";
    public const string Cancelled = "CANCELLED";
    public const string Expired = "EXPIRED";

    /// <summary>Statuses from which the guest may (re)start a checkout.</summary>
    public static readonly IReadOnlyList<string> Payable = [Created, Pending, Failed, Cancelled];

    /// <summary>Statuses closed when the booking is cancelled or expires.</summary>
    public static readonly IReadOnlyList<string> Open = [Created, Pending];
}

public static class PaymentPurposes
{
    public const string Deposit = "DEPOSIT";
}

public static class DepositPolicies
{
    public const string None = "NONE";
    public const string PerBooking = "PER_BOOKING";
    public const string PerGuest = "PER_GUEST";

    public static bool IsValid(string? policy) => policy is None or PerBooking or PerGuest;
}

public static class RefundStatus
{
    public const string Pending = "PENDING";
    public const string Succeeded = "SUCCEEDED";
    public const string Failed = "FAILED";
}

/// <summary>Provider-neutral event types a provider adapter translates its webhooks into.</summary>
public static class PaymentEventTypes
{
    public const string PaymentSucceeded = "payment.succeeded";
    public const string PaymentFailed = "payment.failed";
    public const string PaymentCancelled = "payment.cancelled";
    public const string RefundSucceeded = "refund.succeeded";
    public const string RefundFailed = "refund.failed";
}

public static class WebhookOutcomes
{
    public const string Applied = "APPLIED";
    public const string Ignored = "IGNORED";
    public const string Unmatched = "UNMATCHED";
}

public static class PaymentAuditActions
{
    public const string SettingsUpdated = "payment.settings.updated";
    public const string CheckoutStarted = "payment.checkout_started";
    public const string Succeeded = "payment.succeeded";
    public const string Failed = "payment.failed";
    public const string Flagged = "payment.flagged";
    public const string RefundRequested = "payment.refund_requested";
    public const string Refunded = "payment.refunded";
    public const string RefundFailed = "payment.refund_failed";
}

// ---- Provider SPI: everything a payment provider adapter (PayFast, Peach Payments, Ozow, ...) must implement. ----

/// <summary>
/// A pluggable payment provider. Implementations translate between this neutral contract and one provider's API; the
/// booking model never sees provider types. Our payment id is always sent as the merchant reference, so webhooks are
/// matched to payments without trusting provider-specific ids.
/// </summary>
public interface IPaymentProvider
{
    /// <summary>Stable name stored on payments and used in the webhook route (<c>/api/v1/payments/webhooks/{name}</c>).</summary>
    string Name { get; }

    /// <summary>Starts a hosted checkout. Throws <see cref="PaymentProviderException"/> when the provider cannot be reached.</summary>
    Task<ProviderCheckout> CreateCheckoutAsync(ProviderCheckoutRequest request, CancellationToken cancellationToken);

    /// <summary>Requests a refund. A definite rejection is returned as <see cref="RefundStatus.Failed"/>; an unknown outcome throws.</summary>
    Task<ProviderRefundResult> RefundAsync(ProviderRefundRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Authenticates a webhook (signature, timestamp/replay window, source) and translates it into neutral events.
    /// Must not throw for malformed input: return <see cref="WebhookVerification.Rejected"/>.
    /// </summary>
    WebhookVerification VerifyWebhook(PaymentWebhookRequest request);
}

public sealed record ProviderCheckoutRequest(
    Guid PaymentId,
    long AmountMinor,
    string Currency,
    string Description,
    string ReturnUrl,
    DateTime ExpiresAt);

public sealed record ProviderCheckout(string ProviderReference, string RedirectUrl);

public sealed record ProviderRefundRequest(Guid RefundId, Guid PaymentId, string? ProviderPaymentReference, long AmountMinor, string Currency);

public sealed record ProviderRefundResult(string Status, string? ProviderReference, string? FailureReason);

public sealed record PaymentWebhookRequest(IReadOnlyDictionary<string, string> Headers, byte[] Body);

public sealed record PaymentProviderEvent(
    string EventId,
    string Type,
    Guid PaymentId,
    string? ProviderReference,
    long? AmountMinor,
    string? Currency,
    Guid? RefundId = null,
    string? FailureReason = null);

public sealed record WebhookVerification(bool IsAuthentic, IReadOnlyList<PaymentProviderEvent> Events, string? RejectionReason)
{
    public static WebhookVerification Rejected(string reason) => new(false, [], reason);

    public static WebhookVerification Accepted(IReadOnlyList<PaymentProviderEvent> events) => new(true, events, null);
}

/// <summary>The provider could not be reached or returned an unexpected response; the outcome is unknown.</summary>
public sealed class PaymentProviderException(string message, Exception? innerException = null) : Exception(message, innerException);

public interface IPaymentProviderRegistry
{
    /// <summary>The provider new payments use, or null when payments are not configured.</summary>
    IPaymentProvider? Active { get; }

    IPaymentProvider? Find(string name);
}

public sealed partial class PaymentOptions
{
    public const string SectionName = "Payments";

    /// <summary>Provider name for new payments. Empty disables online payments (venues requiring a deposit then refuse online bookings).</summary>
    public string? Provider { get; set; }

    /// <summary>Origin of the web app, used for checkout return URLs.</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    public static bool IsValid(PaymentOptions options) =>
        options is not null
        && (string.IsNullOrWhiteSpace(options.Provider) || ProviderName().IsMatch(options.Provider))
        && (string.IsNullOrWhiteSpace(options.Provider) || IsOrigin(options.PublicBaseUrl));

    private static bool IsOrigin(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && uri.AbsolutePath == "/" && !value!.EndsWith('/') && string.IsNullOrEmpty(uri.Query);

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]{0,39}$")]
    public static partial Regex ProviderName();
}

// ---- Settings, records and DTOs ----

public sealed record VenuePaymentSettings(string DepositPolicy, long? DepositAmountMinor, string Currency, int PaymentWindowMinutes)
{
    public static VenuePaymentSettings Default { get; } = new(DepositPolicies.None, null, "ZAR", 30);

    /// <summary>The deposit owed for a party, or null when the venue takes no deposit.</summary>
    public long? DepositFor(int partySize) => DepositPolicy switch
    {
        DepositPolicies.PerBooking => DepositAmountMinor,
        DepositPolicies.PerGuest => DepositAmountMinor * partySize,
        _ => null,
    };
}

public sealed record VenuePaymentSettingsDto(
    Guid VenueId,
    string DepositPolicy,
    long? DepositAmountMinor,
    string Currency,
    int PaymentWindowMinutes,
    bool OnlinePaymentsAvailable);

public sealed record UpdateVenuePaymentSettingsRequest(string DepositPolicy, long? DepositAmountMinor, string Currency, int PaymentWindowMinutes);

/// <summary>A deposit to create with a booking, inside the booking transaction.</summary>
public sealed record DepositRequirement(string Provider, long AmountMinor, string Currency, DateTime ExpiresAt);

public sealed record NewPayment(Guid Id, Guid TenantId, Guid VenueId, Guid BookingId, string Purpose, string Provider, long AmountMinor, string Currency, DateTime ExpiresAt, DateTime Now);

public sealed record PaymentRecord(
    Guid Id,
    Guid TenantId,
    Guid VenueId,
    Guid BookingId,
    string Purpose,
    string Provider,
    string? ProviderReference,
    long AmountMinor,
    string Currency,
    string Status,
    long RefundedAmountMinor,
    string? FailureReason,
    bool RequiresAttention,
    DateTime ExpiresAt,
    DateTime? SucceededAt,
    int Version,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record RefundRecord(
    Guid Id,
    Guid TenantId,
    Guid PaymentId,
    long AmountMinor,
    string Status,
    string? ProviderReference,
    string? Reason,
    string? FailureReason,
    Guid? RequestedByUserId,
    DateTime CreatedAt,
    DateTime? CompletedAt);

public sealed record RefundDto(Guid Id, long AmountMinor, string Status, string? Reason, string? FailureReason, DateTime CreatedAt, DateTime? CompletedAt);

public sealed record PaymentDto(
    Guid Id,
    Guid BookingId,
    string Purpose,
    string Provider,
    string? ProviderReference,
    long AmountMinor,
    string Currency,
    string Status,
    long RefundedAmountMinor,
    long RefundableAmountMinor,
    string? FailureReason,
    bool RequiresAttention,
    DateTime ExpiresAt,
    DateTime? SucceededAt,
    DateTime CreatedAt,
    IReadOnlyList<RefundDto> Refunds);

public sealed record RefundRequest(long? AmountMinor, string? Reason);

/// <summary>What a guest sees about the deposit on their booking.</summary>
public sealed record PublicPaymentDto(string Status, long AmountMinor, string Currency, DateTime PayBy, bool CanPay);

public sealed record PublicCheckoutDto(string RedirectUrl);

public sealed record WebhookEventEntry(string Provider, string ProviderEventId, string EventType, byte[] PayloadSha256);

public sealed record DueHeldBooking(Guid TenantId, Guid BookingId);

public interface IPaymentRepository
{
    Task<VenuePaymentSettings> GetSettingsAsync(Guid tenantId, Guid venueId, CancellationToken cancellationToken);

    Task UpsertSettingsAsync(Guid tenantId, Guid venueId, VenuePaymentSettings settings, DateTime now, CancellationToken cancellationToken);

    Task InsertAsync(NewPayment payment, CancellationToken cancellationToken);

    Task<PaymentRecord?> GetAsync(Guid tenantId, Guid paymentId, CancellationToken cancellationToken);

    /// <summary>Webhooks carry no tenant: the payment id (our merchant reference) is the only lookup key.</summary>
    Task<PaymentRecord?> FindByIdAsync(Guid paymentId, CancellationToken cancellationToken);

    Task<PaymentRecord?> LockAsync(Guid tenantId, Guid paymentId, CancellationToken cancellationToken);

    /// <summary>The newest deposit of a booking (there is at most one live deposit).</summary>
    Task<PaymentRecord?> GetLatestDepositAsync(Guid tenantId, Guid bookingId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PaymentRecord>> ListForBookingAsync(Guid tenantId, Guid bookingId, CancellationToken cancellationToken);

    Task<IReadOnlyList<RefundRecord>> ListRefundsAsync(Guid tenantId, IReadOnlyCollection<Guid> paymentIds, CancellationToken cancellationToken);

    /// <summary>Records a started checkout; false when the payment is no longer payable (it succeeded or was closed meanwhile).</summary>
    Task<bool> MarkPendingAsync(Guid tenantId, Guid paymentId, string providerReference, DateTime now, CancellationToken cancellationToken);

    Task MarkSucceededAsync(Guid tenantId, Guid paymentId, string? providerReference, DateTime now, CancellationToken cancellationToken);

    Task MarkUnsuccessfulAsync(Guid tenantId, Guid paymentId, string status, string? reason, DateTime now, CancellationToken cancellationToken);

    Task FlagAsync(Guid tenantId, Guid paymentId, string reason, DateTime now, CancellationToken cancellationToken);

    /// <summary>Closes the booking's unpaid payments with <paramref name="status"/> (CANCELLED or EXPIRED).</summary>
    Task<int> CloseOpenAsync(Guid tenantId, Guid bookingId, string status, string reason, DateTime now, CancellationToken cancellationToken);

    /// <summary>Sum of pending and succeeded refunds: what is no longer available to refund.</summary>
    Task<long> SumReservedRefundsAsync(Guid tenantId, Guid paymentId, CancellationToken cancellationToken);

    Task InsertRefundAsync(RefundRecord refund, CancellationToken cancellationToken);

    Task<RefundRecord?> LockRefundAsync(Guid tenantId, Guid refundId, CancellationToken cancellationToken);

    /// <summary>Settles a pending refund; a success adds its amount to the payment's refunded total.</summary>
    Task<bool> CompleteRefundAsync(Guid tenantId, Guid refundId, string status, string? providerReference, string? failureReason, DateTime now, CancellationToken cancellationToken);

    /// <summary>Claims a webhook event id; false when it was already received (redelivery).</summary>
    Task<bool> TryClaimWebhookEventAsync(WebhookEventEntry entry, CancellationToken cancellationToken);

    Task SetWebhookOutcomeAsync(string provider, string providerEventId, Guid? tenantId, Guid? paymentId, string outcome, string? detail, CancellationToken cancellationToken);

    /// <summary>HELD bookings whose payment window has closed, across tenants (Worker).</summary>
    Task<IReadOnlyList<DueHeldBooking>> ListDueHeldBookingsAsync(DateTime now, int limit, CancellationToken cancellationToken);
}
