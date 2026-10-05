using BookingPlatform.Application.Common.Auditing;
using BookingPlatform.Application.Common.Errors;
using BookingPlatform.Application.Common.Outbox;
using BookingPlatform.Application.Common.Persistence;
using BookingPlatform.Application.Payments;
using BookingPlatform.Application.Venues;
using BookingPlatform.Domain.Bookings;
using IClock = BookingPlatform.Application.Common.Time.IClock;

namespace BookingPlatform.Application.Bookings;

/// <summary>Who applies a status command and why. <c>Precondition</c> runs against the locked booking before anything changes.</summary>
public sealed record TransitionContext(string HistorySource, Guid? UserId, string? Reason = null, int? ExpectedVersion = null)
{
    public Action<BookingRecord>? Precondition { get; init; }
}

/// <summary>
/// Applies booking status commands (DOMAIN_MODEL §4). Shared by staff commands, guest cancellation, payment confirmation
/// and the Worker's deposit expiry, so every path writes the same history, event, audit, outbox and payment changes.
/// </summary>
public sealed class BookingTransitions(
    IBookingRepository bookings,
    IAllocationRepository allocations,
    IVenueRepository venues,
    IPaymentRepository payments,
    IAuditWriter audit,
    IOutboxWriter outbox,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    private const string NotFoundMessage = "The booking was not found.";

    /// <summary>Runs the command in its own transaction (retried once on a transient failure).</summary>
    public Task<BookingRecord> ApplyAsync(Guid tenantId, Guid id, BookingCommand command, TransitionContext context, CancellationToken cancellationToken) =>
        TransientRetry.ExecuteAsync(
            () => unitOfWork.ExecuteAsync(() => ApplyInTransactionAsync(tenantId, id, command, context, cancellationToken), cancellationToken),
            ConcurrencyConflict);

    /// <summary>Runs the command inside the caller's open transaction.</summary>
    public async Task<BookingRecord> ApplyInTransactionAsync(Guid tenantId, Guid id, BookingCommand command, TransitionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var locked = await bookings.LockAsync(tenantId, id, cancellationToken)
            ?? throw new NotFoundAppException(ErrorCodes.BookingNotFound, NotFoundMessage);
        EnsureVersion(context.ExpectedVersion, locked.Version);
        if (command == BookingCommand.Cancel && locked.Status == BookingStatus.Cancelled)
        {
            throw new ConflictAppException(ErrorCodes.BookingAlreadyCancelled, "The booking is already cancelled.");
        }

        var to = BookingStatusMachine.TryApply(locked.Status, command)
            ?? throw new ConflictAppException(ErrorCodes.InvalidStatusTransition,
                $"A {Describe(locked.Status)} booking cannot be {Describe(command)}.");

        var before = await LoadRecordAsync(tenantId, id, cancellationToken);
        context.Precondition?.Invoke(before);
        var now = clock.UtcNow;
        if (command == BookingCommand.NoShow && now < before.StartsAt)
        {
            throw new ConflictAppException(ErrorCodes.InvalidStatusTransition, "A booking cannot be marked as a no-show before its start time.");
        }

        var cancels = command is BookingCommand.Cancel or BookingCommand.Expire;
        await bookings.UpdateStatusAsync(tenantId, id, to, cancels ? context.Reason : null, now, cancellationToken);
        switch (command)
        {
            case BookingCommand.Cancel:
                await allocations.DeactivateAsync(tenantId, id, null, null, ReleaseReasons.Cancelled, now, cancellationToken);
                await payments.CloseOpenAsync(tenantId, id, PaymentStatus.Cancelled, "The booking was cancelled.", now, cancellationToken);
                break;
            case BookingCommand.Expire:
                await allocations.DeactivateAsync(tenantId, id, null, null, ReleaseReasons.Expired, now, cancellationToken);
                await payments.CloseOpenAsync(tenantId, id, PaymentStatus.Expired, "The payment window closed.", now, cancellationToken);
                break;
            case BookingCommand.NoShow:
                await allocations.DeactivateAsync(tenantId, id, null, null, ReleaseReasons.NoShow, now, cancellationToken);
                break;
            case BookingCommand.Complete:
                var venue = await venues.GetAsync(tenantId, before.VenueId, cancellationToken);
                var turnover = TimeSpan.FromMinutes(venue?.Rules.TurnoverBufferMinutes ?? 0);
                var releaseAt = Max(now + turnover, before.StartsAt.AddMinutes(1));
                await allocations.TruncateBookingAsync(tenantId, id, releaseAt, cancellationToken);
                break;
        }

        await bookings.WriteStatusHistoryAsync(new StatusHistoryEntry(tenantId, before.VenueId, id, locked.Status, to, context.HistorySource, context.Reason, context.UserId), cancellationToken);
        await bookings.WriteEventAsync(new BookingEventEntry(tenantId, before.VenueId, id,
            cancels ? BookingEventTypes.Cancelled : BookingEventTypes.StatusChanged,
            new { from = locked.Status, to, reason = context.Reason, plannedEndsAt = before.EndsAt, expired = command == BookingCommand.Expire }, context.UserId), cancellationToken);

        var after = await LoadRecordAsync(tenantId, id, cancellationToken);
        await audit.WriteAsync(new AuditEntry(tenantId, BookingService.EntityType, id,
            cancels ? AuditActions.Cancelled : AuditActions.StatusChanged, BookingService.Snapshot(before), BookingService.Snapshot(after)), cancellationToken);
        await outbox.WriteRealtimeAsync(tenantId,
            cancels ? RealtimeEvents.BookingCancelled : RealtimeEvents.BookingStatusChanged,
            after.VenueId, id, after.Version, after.BusinessDate, now, cancellationToken);
        if (cancels && after.CustomerId is not null)
        {
            await outbox.WriteNotificationAsync(tenantId, NotificationTypes.BookingCancellation, after.VenueId, id, now, cancellationToken);
        }
        else if (command == BookingCommand.Confirm && locked.Status is BookingStatus.Pending or BookingStatus.Held && after.CustomerId is not null)
        {
            await outbox.WriteNotificationAsync(tenantId, NotificationTypes.BookingConfirmation, after.VenueId, id, now, cancellationToken);
        }

        return after;
    }

    internal static void EnsureVersion(int? expected, int actual)
    {
        if (expected is { } version && version != actual)
        {
            throw new ConflictAppException(ErrorCodes.ConcurrencyConflict, "The booking was changed by someone else. Reload and try again.");
        }
    }

    internal static Exception ConcurrencyConflict() =>
        new ConflictAppException(ErrorCodes.ConcurrencyConflict, "The booking is being changed by someone else. Try again.");

    internal static string Describe(string status) => status.ToLowerInvariant().Replace('_', ' ');

    private static string Describe(BookingCommand command) => command switch
    {
        BookingCommand.Confirm => "confirmed",
        BookingCommand.CheckIn => "checked in",
        BookingCommand.Seat => "seated",
        BookingCommand.Complete => "completed",
        BookingCommand.NoShow => "marked as a no-show",
        BookingCommand.Cancel => "cancelled",
        BookingCommand.Expire => "expired",
        _ => "changed",
    };

    private async Task<BookingRecord> LoadRecordAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        await bookings.GetAsync(tenantId, id, cancellationToken)
        ?? throw new NotFoundAppException(ErrorCodes.BookingNotFound, NotFoundMessage);

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
}
