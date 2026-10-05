using BookingPlatform.Application.Payments;
using BookingPlatform.Infrastructure.Persistence;

namespace BookingPlatform.Infrastructure.Payments;

public sealed class PaymentRepository(DbSession db) : IPaymentRepository
{
    private const string SelectPayment = """
        SELECT id, tenant_id, venue_id, booking_id, purpose, provider, provider_reference, amount_minor, currency, status,
               refunded_amount_minor, failure_reason, requires_attention, expires_at, succeeded_at, version, created_at, updated_at
        FROM payments
        """;

    private const string SelectRefund = """
        SELECT id, tenant_id, payment_id, amount_minor, status, provider_reference, reason, failure_reason, requested_by_user_id,
               created_at, completed_at
        FROM payment_refunds
        """;

    public async Task<VenuePaymentSettings> GetSettingsAsync(Guid tenantId, Guid venueId, CancellationToken cancellationToken) =>
        await db.QuerySingleOrDefaultAsync<VenuePaymentSettings>(
            """
            SELECT deposit_policy, deposit_amount_minor, currency, payment_window_minutes
            FROM venue_payment_settings WHERE tenant_id = @tenantId AND venue_id = @venueId
            """,
            new { tenantId, venueId }, cancellationToken)
        ?? VenuePaymentSettings.Default;

    public Task UpsertSettingsAsync(Guid tenantId, Guid venueId, VenuePaymentSettings settings, DateTime now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return db.ExecuteAsync(
            """
            INSERT INTO venue_payment_settings (tenant_id, venue_id, deposit_policy, deposit_amount_minor, currency, payment_window_minutes, created_at, updated_at)
            VALUES (@tenantId, @venueId, @DepositPolicy, @DepositAmountMinor, @Currency, @PaymentWindowMinutes, @now, @now)
            ON CONFLICT (tenant_id, venue_id) DO UPDATE SET
                deposit_policy = EXCLUDED.deposit_policy, deposit_amount_minor = EXCLUDED.deposit_amount_minor,
                currency = EXCLUDED.currency, payment_window_minutes = EXCLUDED.payment_window_minutes, updated_at = EXCLUDED.updated_at
            """,
            new { tenantId, venueId, settings.DepositPolicy, settings.DepositAmountMinor, settings.Currency, settings.PaymentWindowMinutes, now },
            cancellationToken);
    }

    public Task InsertAsync(NewPayment payment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payment);
        return db.ExecuteAsync(
            """
            INSERT INTO payments (id, tenant_id, venue_id, booking_id, purpose, provider, amount_minor, currency, status, expires_at, created_at, updated_at)
            VALUES (@Id, @TenantId, @VenueId, @BookingId, @Purpose, @Provider, @AmountMinor, @Currency, 'CREATED', @ExpiresAt, @Now, @Now)
            """,
            payment, cancellationToken);
    }

    public Task<PaymentRecord?> GetAsync(Guid tenantId, Guid paymentId, CancellationToken cancellationToken) =>
        db.QuerySingleOrDefaultAsync<PaymentRecord>($"{SelectPayment} WHERE tenant_id = @tenantId AND id = @paymentId",
            new { tenantId, paymentId }, cancellationToken);

    public Task<PaymentRecord?> FindByIdAsync(Guid paymentId, CancellationToken cancellationToken) =>
        db.QuerySingleOrDefaultAsync<PaymentRecord>($"{SelectPayment} WHERE id = @paymentId", new { paymentId }, cancellationToken);

    public Task<PaymentRecord?> LockAsync(Guid tenantId, Guid paymentId, CancellationToken cancellationToken) =>
        db.QuerySingleOrDefaultAsync<PaymentRecord>($"{SelectPayment} WHERE tenant_id = @tenantId AND id = @paymentId FOR UPDATE",
            new { tenantId, paymentId }, cancellationToken);

    public Task<PaymentRecord?> GetLatestDepositAsync(Guid tenantId, Guid bookingId, CancellationToken cancellationToken) =>
        db.QuerySingleOrDefaultAsync<PaymentRecord>(
            $"{SelectPayment} WHERE tenant_id = @tenantId AND booking_id = @bookingId AND purpose = 'DEPOSIT' ORDER BY created_at DESC, id DESC LIMIT 1",
            new { tenantId, bookingId }, cancellationToken);

    public Task<IReadOnlyList<PaymentRecord>> ListForBookingAsync(Guid tenantId, Guid bookingId, CancellationToken cancellationToken) =>
        db.QueryAsync<PaymentRecord>($"{SelectPayment} WHERE tenant_id = @tenantId AND booking_id = @bookingId ORDER BY created_at, id",
            new { tenantId, bookingId }, cancellationToken);

    public async Task<IReadOnlyList<RefundRecord>> ListRefundsAsync(Guid tenantId, IReadOnlyCollection<Guid> paymentIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paymentIds);
        if (paymentIds.Count == 0)
        {
            return [];
        }

        return await db.QueryAsync<RefundRecord>($"{SelectRefund} WHERE tenant_id = @tenantId AND payment_id = ANY(@paymentIds) ORDER BY created_at, id",
            new { tenantId, paymentIds = paymentIds.ToArray() }, cancellationToken);
    }

    public async Task<bool> MarkPendingAsync(Guid tenantId, Guid paymentId, string providerReference, DateTime now, CancellationToken cancellationToken) =>
        await db.ExecuteAsync(
            """
            UPDATE payments SET status = 'PENDING', provider_reference = @providerReference, failure_reason = NULL,
                                version = version + 1, updated_at = @now
            WHERE tenant_id = @tenantId AND id = @paymentId AND status IN ('CREATED', 'PENDING', 'FAILED', 'CANCELLED') AND expires_at > @now
            """,
            new { tenantId, paymentId, providerReference, now }, cancellationToken) == 1;

    public Task MarkSucceededAsync(Guid tenantId, Guid paymentId, string? providerReference, DateTime now, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            """
            UPDATE payments SET status = 'SUCCEEDED', succeeded_at = @now, provider_reference = coalesce(@providerReference, provider_reference),
                                failure_reason = NULL, version = version + 1, updated_at = @now
            WHERE tenant_id = @tenantId AND id = @paymentId
            """,
            new { tenantId, paymentId, providerReference, now }, cancellationToken);

    public Task MarkUnsuccessfulAsync(Guid tenantId, Guid paymentId, string status, string? reason, DateTime now, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            """
            UPDATE payments SET status = @status, failure_reason = @reason, version = version + 1, updated_at = @now
            WHERE tenant_id = @tenantId AND id = @paymentId
            """,
            new { tenantId, paymentId, status, reason, now }, cancellationToken);

    public Task FlagAsync(Guid tenantId, Guid paymentId, string reason, DateTime now, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            """
            UPDATE payments SET requires_attention = true, failure_reason = left(@reason, 500), version = version + 1, updated_at = @now
            WHERE tenant_id = @tenantId AND id = @paymentId
            """,
            new { tenantId, paymentId, reason, now }, cancellationToken);

    public Task<int> CloseOpenAsync(Guid tenantId, Guid bookingId, string status, string reason, DateTime now, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            """
            UPDATE payments SET status = @status, failure_reason = @reason, version = version + 1, updated_at = @now
            WHERE tenant_id = @tenantId AND booking_id = @bookingId AND status IN ('CREATED', 'PENDING')
            """,
            new { tenantId, bookingId, status, reason, now }, cancellationToken);

    public async Task<long> SumReservedRefundsAsync(Guid tenantId, Guid paymentId, CancellationToken cancellationToken) =>
        await db.ExecuteScalarAsync<long>(
            "SELECT coalesce(sum(amount_minor), 0)::bigint FROM payment_refunds WHERE tenant_id = @tenantId AND payment_id = @paymentId AND status <> 'FAILED'",
            new { tenantId, paymentId }, cancellationToken);

    public Task InsertRefundAsync(RefundRecord refund, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(refund);
        return db.ExecuteAsync(
            """
            INSERT INTO payment_refunds (id, tenant_id, payment_id, amount_minor, status, reason, requested_by_user_id, created_at)
            VALUES (@Id, @TenantId, @PaymentId, @AmountMinor, 'PENDING', @Reason, @RequestedByUserId, @CreatedAt)
            """,
            refund, cancellationToken);
    }

    public Task<RefundRecord?> LockRefundAsync(Guid tenantId, Guid refundId, CancellationToken cancellationToken) =>
        db.QuerySingleOrDefaultAsync<RefundRecord>($"{SelectRefund} WHERE tenant_id = @tenantId AND id = @refundId FOR UPDATE",
            new { tenantId, refundId }, cancellationToken);

    public async Task<bool> CompleteRefundAsync(
        Guid tenantId, Guid refundId, string status, string? providerReference, string? failureReason, DateTime now, CancellationToken cancellationToken)
    {
        var paymentId = await db.ExecuteScalarAsync<Guid?>(
            """
            UPDATE payment_refunds SET status = @status, provider_reference = coalesce(@providerReference, provider_reference),
                                       failure_reason = @failureReason, completed_at = @now
            WHERE tenant_id = @tenantId AND id = @refundId AND status = 'PENDING'
            RETURNING payment_id
            """,
            new { tenantId, refundId, status, providerReference, failureReason, now }, cancellationToken);
        if (paymentId is null)
        {
            return false;
        }

        if (status == RefundStatus.Succeeded)
        {
            await db.ExecuteAsync(
                """
                UPDATE payments SET refunded_amount_minor = refunded_amount_minor + r.amount_minor, version = version + 1, updated_at = @now
                FROM payment_refunds r
                WHERE payments.tenant_id = @tenantId AND payments.id = r.payment_id AND r.id = @refundId
                """,
                new { tenantId, refundId, now }, cancellationToken);
        }

        return true;
    }

    public async Task<bool> TryClaimWebhookEventAsync(WebhookEventEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return await db.ExecuteAsync(
            """
            INSERT INTO payment_webhook_events (provider, provider_event_id, event_type, outcome, payload_sha256)
            VALUES (@Provider, @ProviderEventId, left(@EventType, 60), 'IGNORED', @PayloadSha256)
            ON CONFLICT (provider, provider_event_id) DO NOTHING
            """,
            entry, cancellationToken) == 1;
    }

    public Task SetWebhookOutcomeAsync(
        string provider, string providerEventId, Guid? tenantId, Guid? paymentId, string outcome, string? detail, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            """
            UPDATE payment_webhook_events SET tenant_id = @tenantId, payment_id = @paymentId, outcome = @outcome, detail = left(@detail, 500)
            WHERE provider = @provider AND provider_event_id = @providerEventId
            """,
            new { provider, providerEventId, tenantId, paymentId, outcome, detail }, cancellationToken);

    public Task<IReadOnlyList<DueHeldBooking>> ListDueHeldBookingsAsync(DateTime now, int limit, CancellationToken cancellationToken) =>
        db.QueryAsync<DueHeldBooking>(
            """
            SELECT tenant_id, id AS booking_id FROM bookings
            WHERE status = 'HELD' AND expires_at <= @now
            ORDER BY expires_at
            LIMIT @limit
            """,
            new { now, limit }, cancellationToken);
}
