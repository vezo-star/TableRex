using System.Security.Cryptography;
using BookingPlatform.Application.Bookings;
using BookingPlatform.Application.Common.Auditing;
using BookingPlatform.Application.Common.Errors;
using BookingPlatform.Application.Common.Idempotency;
using BookingPlatform.Application.Common.Persistence;
using BookingPlatform.Application.Common.Security;
using BookingPlatform.Application.Common.Validation;
using BookingPlatform.Application.Venues;
using BookingPlatform.Domain.Bookings;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using IClock = BookingPlatform.Application.Common.Time.IClock;

namespace BookingPlatform.Application.Payments;

/// <summary>
/// Deposits, checkout, webhooks and refunds on top of the provider-neutral <see cref="IPaymentProvider"/> (D-082..D-086).
/// Providers are only ever called outside database transactions; webhooks are the source of truth for payment outcomes.
/// </summary>
public sealed partial class PaymentService(
    IPaymentRepository payments,
    IPaymentProviderRegistry providers,
    IBookingRepository bookings,
    BookingTransitions transitions,
    IVenueAccessGuard guard,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IAuditWriter audit,
    IUnitOfWork unitOfWork,
    IdempotencyGuard idempotency,
    IOptions<PaymentOptions> options,
    IClock clock,
    ILogger<PaymentService> logger,
    IValidator<UpdateVenuePaymentSettingsRequest> settingsValidator,
    IValidator<RefundRequest> refundValidator)
{
    private const string EntityType = "payment";
    private const string PaymentNotFoundMessage = "The payment was not found.";
    private const string ReturnPath = "/booking/payment-return";

    // ---- Venue deposit settings (staff) ----

    public async Task<VenuePaymentSettingsDto> GetSettingsAsync(Guid venueId, CancellationToken cancellationToken)
    {
        await guard.EnsureAccessAsync(venueId, cancellationToken);
        var settings = await payments.GetSettingsAsync(tenantContext.TenantId, venueId, cancellationToken);
        return ToDto(venueId, settings);
    }

    public async Task<VenuePaymentSettingsDto> UpdateSettingsAsync(Guid venueId, UpdateVenuePaymentSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await settingsValidator.EnsureValidAsync(request, cancellationToken);
        await guard.EnsureAccessAsync(venueId, cancellationToken);
        var tenantId = tenantContext.TenantId;
        var settings = new VenuePaymentSettings(request.DepositPolicy, request.DepositPolicy == DepositPolicies.None ? null : request.DepositAmountMinor,
            request.Currency.ToUpperInvariant(), request.PaymentWindowMinutes);

        await unitOfWork.ExecuteAsync(async () =>
        {
            var before = await payments.GetSettingsAsync(tenantId, venueId, cancellationToken);
            await payments.UpsertSettingsAsync(tenantId, venueId, settings, clock.UtcNow, cancellationToken);
            await audit.WriteAsync(new AuditEntry(tenantId, "venue", venueId, PaymentAuditActions.SettingsUpdated, before, settings), cancellationToken);
        }, cancellationToken);
        return ToDto(venueId, settings);
    }

    // ---- Public booking integration ----

    /// <summary>The deposit a new online booking must pay, or null. Throws when a deposit is required but payments are off.</summary>
    internal async Task<DepositRequirement?> QuoteDepositAsync(Guid tenantId, Guid venueId, int partySize, CancellationToken cancellationToken)
    {
        var settings = await payments.GetSettingsAsync(tenantId, venueId, cancellationToken);
        if (settings.DepositFor(partySize) is not { } amount)
        {
            return null;
        }

        var provider = providers.Active ?? throw new ServiceUnavailableAppException(ErrorCodes.PaymentUnavailable,
            "This venue requires a deposit but cannot take payments online right now. Contact the venue to book.");
        return new DepositRequirement(provider.Name, amount, settings.Currency, clock.UtcNow.AddMinutes(settings.PaymentWindowMinutes));
    }

    internal async Task<PublicPaymentDto?> GetPublicPaymentAsync(Guid tenantId, BookingRecord booking, CancellationToken cancellationToken)
    {
        var payment = await payments.GetLatestDepositAsync(tenantId, booking.Id, cancellationToken);
        return payment is null ? null : new PublicPaymentDto(payment.Status, payment.AmountMinor, payment.Currency, payment.ExpiresAt, IsPayable(booking, payment));
    }

    /// <summary>
    /// Starts (or restarts) the provider checkout for a HELD booking's deposit. The provider is called outside any
    /// transaction; the payment then moves to PENDING unless a webhook settled it meanwhile.
    /// </summary>
    internal async Task<PublicCheckoutDto> StartPublicCheckoutAsync(Guid tenantId, BookingRecord booking, string venueName, CancellationToken cancellationToken)
    {
        var payment = await payments.GetLatestDepositAsync(tenantId, booking.Id, cancellationToken);
        if (payment is null || !IsPayable(booking, payment))
        {
            throw new BusinessRuleAppException(ErrorCodes.PaymentNotPayable,
                payment?.Status == PaymentStatus.Succeeded ? "The deposit has already been paid." : "This booking has no deposit to pay.");
        }

        var provider = providers.Find(payment.Provider) ?? throw new ServiceUnavailableAppException(ErrorCodes.PaymentUnavailable,
            "Online payment is not available right now. Contact the venue.");
        ProviderCheckout checkout;
        try
        {
            checkout = await provider.CreateCheckoutAsync(new ProviderCheckoutRequest(payment.Id, payment.AmountMinor, payment.Currency,
                $"Deposit for booking {booking.Reference} at {venueName}", options.Value.PublicBaseUrl + ReturnPath, payment.ExpiresAt), cancellationToken);
        }
        catch (PaymentProviderException ex)
        {
            logger.LogWarning(ex, "Payment provider {Provider} could not start a checkout for payment {PaymentId}", provider.Name, payment.Id);
            throw new ServiceUnavailableAppException(ErrorCodes.PaymentUnavailable, "The payment provider is unavailable. Try again in a moment.");
        }

        var started = await unitOfWork.ExecuteAsync(async () =>
        {
            if (!await payments.MarkPendingAsync(tenantId, payment.Id, checkout.ProviderReference, clock.UtcNow, cancellationToken))
            {
                return false;
            }

            await audit.WriteAsync(new AuditEntry(tenantId, EntityType, payment.Id, PaymentAuditActions.CheckoutStarted,
                new { payment.Status }, new { Status = PaymentStatus.Pending, payment.Provider }), cancellationToken);
            return true;
        }, cancellationToken);
        if (!started)
        {
            throw new BusinessRuleAppException(ErrorCodes.PaymentNotPayable, "The deposit can no longer be paid. Reload the booking.");
        }

        return new PublicCheckoutDto(checkout.RedirectUrl);
    }

    private bool IsPayable(BookingRecord booking, PaymentRecord payment) =>
        booking.Status == BookingStatus.Held
        && PaymentStatus.Payable.Contains(payment.Status)
        && payment.ExpiresAt > clock.UtcNow
        && booking.ExpiresAt > clock.UtcNow;

    // ---- Webhooks ----

    /// <summary>
    /// Authenticates and applies a provider webhook. Unauthenticated requests are rejected before anything is stored;
    /// each event is claimed by (provider, event id) so redelivery is a no-op; every event is logged with its outcome.
    /// </summary>
    public async Task<int> HandleWebhookAsync(string providerName, PaymentWebhookRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var provider = (PaymentOptions.ProviderName().IsMatch(providerName ?? string.Empty) ? providers.Find(providerName!) : null)
            ?? throw new NotFoundAppException(ErrorCodes.NotFound, "Unknown payment provider.");

        var verification = provider.VerifyWebhook(request);
        if (!verification.IsAuthentic)
        {
            logger.LogWarning("Rejected an unauthenticated {Provider} payment webhook: {Reason}", provider.Name, verification.RejectionReason);
            throw new UnauthorizedAppException(ErrorCodes.InvalidWebhookSignature, "The webhook could not be authenticated.");
        }

        var payloadHash = SHA256.HashData(request.Body);
        var applied = 0;
        foreach (var evt in verification.Events)
        {
            if (await ProcessEventAsync(provider.Name, evt, payloadHash, cancellationToken))
            {
                applied++;
            }
        }

        return applied;
    }

    private Task<bool> ProcessEventAsync(string provider, PaymentProviderEvent evt, byte[] payloadHash, CancellationToken cancellationToken) =>
        TransientRetry.ExecuteAsync(
            () => unitOfWork.ExecuteAsync(async () =>
            {
                if (!await payments.TryClaimWebhookEventAsync(new WebhookEventEntry(provider, evt.EventId, evt.Type, payloadHash), cancellationToken))
                {
                    logger.LogInformation("Ignored redelivered {Provider} webhook event {EventId}", provider, evt.EventId);
                    return false;
                }

                var found = await payments.FindByIdAsync(evt.PaymentId, cancellationToken);
                if (found is null || found.Provider != provider)
                {
                    await payments.SetWebhookOutcomeAsync(provider, evt.EventId, null, null, WebhookOutcomes.Unmatched, "No payment with this reference for this provider.", cancellationToken);
                    logger.LogWarning("{Provider} webhook event {EventId} did not match a payment", provider, evt.EventId);
                    return false;
                }

                // Lock order everywhere: booking, then payment (cancellation and expiry take the booking lock first too).
                await bookings.LockAsync(found.TenantId, found.BookingId, cancellationToken);
                var payment = await payments.LockAsync(found.TenantId, found.Id, cancellationToken)
                    ?? throw new NotFoundAppException(ErrorCodes.PaymentNotFound, PaymentNotFoundMessage);
                var (outcome, detail) = evt.Type switch
                {
                    PaymentEventTypes.PaymentSucceeded => await ApplySucceededAsync(payment, evt, cancellationToken),
                    PaymentEventTypes.PaymentFailed => await ApplyUnsuccessfulAsync(payment, PaymentStatus.Failed, evt, cancellationToken),
                    PaymentEventTypes.PaymentCancelled => await ApplyUnsuccessfulAsync(payment, PaymentStatus.Cancelled, evt, cancellationToken),
                    PaymentEventTypes.RefundSucceeded => await ApplyRefundAsync(payment, RefundStatus.Succeeded, evt, cancellationToken),
                    PaymentEventTypes.RefundFailed => await ApplyRefundAsync(payment, RefundStatus.Failed, evt, cancellationToken),
                    _ => (WebhookOutcomes.Ignored, "Unsupported event type."),
                };

                await payments.SetWebhookOutcomeAsync(provider, evt.EventId, payment.TenantId, payment.Id, outcome, detail, cancellationToken);
                logger.LogInformation("{Provider} webhook event {EventId} ({EventType}) for payment {PaymentId}: {Outcome}",
                    provider, evt.EventId, evt.Type, payment.Id, outcome);
                return outcome == WebhookOutcomes.Applied;
            }, cancellationToken),
            () => new ServiceUnavailableAppException(ErrorCodes.PaymentUnavailable, "The webhook could not be processed. Retry later."));

    private async Task<(string Outcome, string Detail)> ApplySucceededAsync(PaymentRecord payment, PaymentProviderEvent evt, CancellationToken cancellationToken)
    {
        if (payment.Status == PaymentStatus.Succeeded)
        {
            return (WebhookOutcomes.Ignored, "The payment had already succeeded.");
        }

        var now = clock.UtcNow;
        if (evt.AmountMinor != payment.AmountMinor || !string.Equals(evt.Currency, payment.Currency, StringComparison.OrdinalIgnoreCase))
        {
            const string mismatch = "The provider reported a different amount or currency; check the payment with the provider.";
            await payments.FlagAsync(payment.TenantId, payment.Id, mismatch, now, cancellationToken);
            await audit.WriteAsync(new AuditEntry(payment.TenantId, EntityType, payment.Id, PaymentAuditActions.Flagged, null,
                new { Reason = mismatch, ReportedAmountMinor = evt.AmountMinor, ReportedCurrency = evt.Currency }), cancellationToken);
            return (WebhookOutcomes.Ignored, "Amount or currency mismatch.");
        }

        await payments.MarkSucceededAsync(payment.TenantId, payment.Id, evt.ProviderReference, now, cancellationToken);
        await audit.WriteAsync(new AuditEntry(payment.TenantId, EntityType, payment.Id, PaymentAuditActions.Succeeded,
            new { payment.Status }, new { Status = PaymentStatus.Succeeded, payment.AmountMinor, payment.Currency }), cancellationToken);

        var booking = await bookings.GetAsync(payment.TenantId, payment.BookingId, cancellationToken);
        if (booking?.Status == BookingStatus.Held)
        {
            await transitions.ApplyInTransactionAsync(payment.TenantId, payment.BookingId, BookingCommand.Confirm,
                new TransitionContext(HistorySources.System, null, "Deposit paid."), cancellationToken);
            return (WebhookOutcomes.Applied, "Payment succeeded; booking confirmed.");
        }

        if (booking is null || booking.Status is BookingStatus.Cancelled or BookingStatus.NoShow)
        {
            var reason = $"Paid after the booking was {(booking is null ? "removed" : BookingTransitions.Describe(booking.Status))}; refund the guest or rebook.";
            await payments.FlagAsync(payment.TenantId, payment.Id, reason, now, cancellationToken);
            await audit.WriteAsync(new AuditEntry(payment.TenantId, EntityType, payment.Id, PaymentAuditActions.Flagged, null, new { Reason = reason }), cancellationToken);
            return (WebhookOutcomes.Applied, "Payment succeeded for an inactive booking; flagged for staff.");
        }

        return (WebhookOutcomes.Applied, $"Payment succeeded; booking already {BookingTransitions.Describe(booking.Status)}.");
    }

    private async Task<(string Outcome, string Detail)> ApplyUnsuccessfulAsync(PaymentRecord payment, string status, PaymentProviderEvent evt, CancellationToken cancellationToken)
    {
        if (payment.Status is not (PaymentStatus.Created or PaymentStatus.Pending))
        {
            return (WebhookOutcomes.Ignored, $"The payment is {payment.Status}.");
        }

        var reason = Truncate(evt.FailureReason) ?? (status == PaymentStatus.Cancelled ? "The guest cancelled the payment." : "The payment failed.");
        await payments.MarkUnsuccessfulAsync(payment.TenantId, payment.Id, status, reason, clock.UtcNow, cancellationToken);
        await audit.WriteAsync(new AuditEntry(payment.TenantId, EntityType, payment.Id, PaymentAuditActions.Failed,
            new { payment.Status }, new { Status = status, Reason = reason }), cancellationToken);
        return (WebhookOutcomes.Applied, $"Payment {status.ToLowerInvariant()}.");
    }

    private async Task<(string Outcome, string Detail)> ApplyRefundAsync(PaymentRecord payment, string status, PaymentProviderEvent evt, CancellationToken cancellationToken)
    {
        var refund = evt.RefundId is { } refundId ? await payments.LockRefundAsync(payment.TenantId, refundId, cancellationToken) : null;
        if (refund is null || refund.PaymentId != payment.Id)
        {
            return (WebhookOutcomes.Ignored, "Unknown refund.");
        }

        if (refund.Status != RefundStatus.Pending)
        {
            return (WebhookOutcomes.Ignored, $"The refund is already {refund.Status}.");
        }

        await SettleRefundAsync(payment, refund, new ProviderRefundResult(status, evt.ProviderReference, Truncate(evt.FailureReason)), cancellationToken);
        return (WebhookOutcomes.Applied, $"Refund {status.ToLowerInvariant()}.");
    }

    // ---- Staff: payments and refunds ----

    public async Task<IReadOnlyList<PaymentDto>> ListForBookingAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var booking = await GetAccessibleBookingAsync(bookingId, cancellationToken);
        var tenantId = tenantContext.TenantId;
        var list = await payments.ListForBookingAsync(tenantId, booking.Id, cancellationToken);
        var refunds = await payments.ListRefundsAsync(tenantId, list.Select(p => p.Id).ToList(), cancellationToken);
        return list.Select(p => ToDto(p, refunds.Where(r => r.PaymentId == p.Id).ToList())).ToList();
    }

    /// <summary>
    /// Refunds part or all of a succeeded payment. The refund row is reserved first (so concurrent refunds can never exceed
    /// the payment), the provider is called outside the transaction, and an unknown provider outcome leaves the refund
    /// PENDING and flags the payment instead of guessing.
    /// </summary>
    public async Task<IdempotentResult<RefundDto>> RefundAsync(
        Guid bookingId, Guid paymentId, RefundRequest request, IdempotencyRequest idempotencyRequest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await refundValidator.EnsureValidAsync(request, cancellationToken);
        var booking = await GetAccessibleBookingAsync(bookingId, cancellationToken);
        var tenantId = tenantContext.TenantId;
        var userId = currentUser.RequireUserId();
        var scope = $"payment.refund:user:{userId}";

        var reserved = await unitOfWork.ExecuteAsync(async () =>
        {
            var replay = await idempotency.BeginAsync(tenantId, scope, idempotencyRequest, cancellationToken);
            if (replay is not null)
            {
                return (Replay: replay, Payment: (PaymentRecord?)null, Refund: (RefundRecord?)null);
            }

            await bookings.LockAsync(tenantId, booking.Id, cancellationToken);
            var payment = await payments.LockAsync(tenantId, paymentId, cancellationToken);
            if (payment is null || payment.BookingId != booking.Id)
            {
                throw new NotFoundAppException(ErrorCodes.PaymentNotFound, PaymentNotFoundMessage);
            }

            if (payment.Status != PaymentStatus.Succeeded)
            {
                throw new BusinessRuleAppException(ErrorCodes.RefundNotAllowed, "Only a paid payment can be refunded.");
            }

            var refundable = payment.AmountMinor - await payments.SumReservedRefundsAsync(tenantId, payment.Id, cancellationToken);
            var amount = request.AmountMinor ?? refundable;
            if (refundable <= 0 || amount > refundable)
            {
                throw new BusinessRuleAppException(ErrorCodes.RefundNotAllowed,
                    refundable <= 0 ? "The payment has already been fully refunded." : $"At most {refundable} (minor units) can still be refunded.");
            }

            var refund = new RefundRecord(Guid.CreateVersion7(), tenantId, payment.Id, amount, RefundStatus.Pending, null, Clean(request.Reason), null, userId, clock.UtcNow, null);
            await payments.InsertRefundAsync(refund, cancellationToken);
            await audit.WriteAsync(new AuditEntry(tenantId, EntityType, payment.Id, PaymentAuditActions.RefundRequested, null,
                new { RefundId = refund.Id, refund.AmountMinor, payment.Currency, refund.Reason }), cancellationToken);
            return (Replay: (IdempotentReplay?)null, Payment: (PaymentRecord?)payment, Refund: (RefundRecord?)refund);
        }, cancellationToken);

        if (reserved.Replay is not null)
        {
            return new IdempotentResult<RefundDto>(null, reserved.Replay);
        }

        var (payment, refund) = (reserved.Payment!, reserved.Refund!);
        ProviderRefundResult? result = null;
        var provider = providers.Find(payment.Provider);
        if (provider is not null)
        {
            try
            {
                result = await provider.RefundAsync(new ProviderRefundRequest(refund.Id, payment.Id, payment.ProviderReference, refund.AmountMinor, payment.Currency),
                    CancellationToken.None);
            }
            catch (PaymentProviderException ex)
            {
                logger.LogWarning(ex, "Refund {RefundId} for payment {PaymentId}: provider {Provider} outcome unknown", refund.Id, payment.Id, provider.Name);
            }
        }

        var dto = await unitOfWork.ExecuteAsync(async () =>
        {
            var current = await payments.LockAsync(tenantId, payment.Id, CancellationToken.None) ?? payment;
            var locked = await payments.LockRefundAsync(tenantId, refund.Id, CancellationToken.None) ?? refund;
            if (result is not null && result.Status != RefundStatus.Pending && locked.Status == RefundStatus.Pending)
            {
                await SettleRefundAsync(current, locked, result, CancellationToken.None);
            }
            else if (result is null)
            {
                const string unknown = "A refund outcome is unknown; check it with the payment provider.";
                await payments.FlagAsync(tenantId, payment.Id, unknown, clock.UtcNow, CancellationToken.None);
                await audit.WriteAsync(new AuditEntry(tenantId, EntityType, payment.Id, PaymentAuditActions.Flagged, null, new { Reason = unknown, RefundId = refund.Id }),
                    CancellationToken.None);
            }

            var settled = await payments.LockRefundAsync(tenantId, refund.Id, CancellationToken.None) ?? locked;
            var response = ToDto(settled);
            await idempotency.CompleteAsync(tenantId, scope, idempotencyRequest, 201, response, refund.Id, CancellationToken.None);
            return response;
        }, CancellationToken.None);

        return new IdempotentResult<RefundDto>(dto, null);
    }

    private async Task SettleRefundAsync(PaymentRecord payment, RefundRecord refund, ProviderRefundResult result, CancellationToken cancellationToken)
    {
        var failure = result.Status == RefundStatus.Failed ? Truncate(result.FailureReason) ?? "The provider declined the refund." : null;
        await payments.CompleteRefundAsync(payment.TenantId, refund.Id, result.Status, result.ProviderReference, failure, clock.UtcNow, cancellationToken);
        await audit.WriteAsync(new AuditEntry(payment.TenantId, EntityType, payment.Id,
            result.Status == RefundStatus.Succeeded ? PaymentAuditActions.Refunded : PaymentAuditActions.RefundFailed, null,
            new { RefundId = refund.Id, refund.AmountMinor, payment.Currency, Reason = failure }), cancellationToken);
    }

    private async Task<BookingRecord> GetAccessibleBookingAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var booking = await bookings.GetAsync(tenantContext.TenantId, bookingId, cancellationToken)
            ?? throw new NotFoundAppException(ErrorCodes.BookingNotFound, "The booking was not found.");
        try
        {
            await guard.EnsureAccessAsync(booking.VenueId, cancellationToken);
        }
        catch (NotFoundAppException)
        {
            throw new NotFoundAppException(ErrorCodes.BookingNotFound, "The booking was not found.");
        }

        return booking;
    }

    private VenuePaymentSettingsDto ToDto(Guid venueId, VenuePaymentSettings s) =>
        new(venueId, s.DepositPolicy, s.DepositAmountMinor, s.Currency, s.PaymentWindowMinutes, providers.Active is not null);

    private static PaymentDto ToDto(PaymentRecord p, IReadOnlyList<RefundRecord> refunds)
    {
        var reserved = refunds.Where(r => r.Status != RefundStatus.Failed).Sum(r => r.AmountMinor);
        var refundable = p.Status == PaymentStatus.Succeeded ? Math.Max(0, p.AmountMinor - reserved) : 0;
        return new PaymentDto(p.Id, p.BookingId, p.Purpose, p.Provider, p.ProviderReference, p.AmountMinor, p.Currency, p.Status, p.RefundedAmountMinor,
            refundable, p.FailureReason, p.RequiresAttention, p.ExpiresAt, p.SucceededAt, p.CreatedAt, refunds.Select(ToDto).ToList());
    }

    private static RefundDto ToDto(RefundRecord r) => new(r.Id, r.AmountMinor, r.Status, r.Reason, r.FailureReason, r.CreatedAt, r.CompletedAt);

    private static string? Truncate(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= 500 ? value.Trim() : value.Trim()[..500];

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class UpdateVenuePaymentSettingsRequestValidator : AbstractValidator<UpdateVenuePaymentSettingsRequest>
{
    public UpdateVenuePaymentSettingsRequestValidator()
    {
        RuleFor(r => r.DepositPolicy).Must(DepositPolicies.IsValid).WithMessage("Deposit policy must be NONE, PER_BOOKING or PER_GUEST.");
        RuleFor(r => r.DepositAmountMinor)
            .NotNull().WithMessage("Enter the deposit amount.")
            .InclusiveBetween(100, 10_000_000).WithMessage("The deposit must be between 1.00 and 100 000.00.")
            .When(r => r.DepositPolicy is DepositPolicies.PerBooking or DepositPolicies.PerGuest);
        RuleFor(r => r.Currency).NotEmpty().Matches("^[A-Za-z]{3}$").WithMessage("Currency must be a 3-letter ISO code.");
        RuleFor(r => r.PaymentWindowMinutes).InclusiveBetween(5, 1440).WithMessage("The payment window must be 5-1440 minutes.");
    }
}

public sealed class RefundRequestValidator : AbstractValidator<RefundRequest>
{
    public RefundRequestValidator()
    {
        RuleFor(r => r.AmountMinor).GreaterThan(0).When(r => r.AmountMinor is not null);
        RuleFor(r => r.Reason).MaximumLength(500);
    }
}
