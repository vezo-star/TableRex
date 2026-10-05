using System.Text.RegularExpressions;
using BookingPlatform.Application.Common.Auditing;
using BookingPlatform.Application.Common.Errors;
using BookingPlatform.Application.Common.Idempotency;
using BookingPlatform.Application.Common.Outbox;
using BookingPlatform.Application.Common.Pagination;
using BookingPlatform.Application.Common.Persistence;
using BookingPlatform.Application.Common.Security;
using BookingPlatform.Application.Common.Validation;
using BookingPlatform.Application.Payments;
using BookingPlatform.Application.Venues;
using BookingPlatform.Domain.Bookings;
using BookingPlatform.Domain.Time;
using BookingPlatform.Domain.Venues;
using FluentValidation;
using NodaTime;
using IClock = BookingPlatform.Application.Common.Time.IClock;

namespace BookingPlatform.Application.Bookings;

/// <summary>
/// The booking engine (DOMAIN_MODEL §4, DATABASE.md §4). Every command runs in one transaction that writes the booking,
/// its allocations, status history, booking event, audit row and outbox messages together.
/// </summary>
public sealed partial class BookingService(
    IBookingRepository bookings,
    IAllocationRepository allocations,
    IHoldRepository holds,
    IBookingCustomerRepository customers,
    IPaymentRepository payments,
    BookingAllocator allocator,
    BookingTransitions transitions,
    IVenueRepository venues,
    VenueService venueService,
    IVenueAccessGuard guard,
    IdempotencyGuard idempotency,
    IAuditWriter audit,
    IOutboxWriter outbox,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IClock clock,
    IValidator<CreateBookingRequest> createValidator,
    IValidator<UpdateBookingRequest> updateValidator,
    IValidator<MoveBookingRequest> moveValidator,
    IValidator<CancelBookingRequest> cancelValidator,
    IValidator<BookingListQuery> listValidator)
{
    internal const string EntityType = "booking";
    private const string NotFoundMessage = "The booking was not found.";

    public async Task<PagedResult<BookingDto>> ListAsync(BookingListQuery query, CancellationToken cancellationToken)
    {
        await listValidator.EnsureValidAsync(query, cancellationToken);
        IReadOnlyCollection<Guid>? venueIds;
        if (query.VenueId is { } venueId)
        {
            await guard.EnsureAccessAsync(venueId, cancellationToken);
            venueIds = [venueId];
        }
        else
        {
            venueIds = await guard.GetAccessibleVenueIdsAsync(cancellationToken);
        }

        var page = await bookings.ListAsync(tenantContext.TenantId, query, venueIds, cancellationToken);
        return new PagedResult<BookingDto>(page.Items.Select(ToDto).ToList(), page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<BookingDto> GetAsync(Guid id, CancellationToken cancellationToken) =>
        ToDto(await GetAccessibleAsync(id, cancellationToken));

    public async Task<BookingHistoryDto> GetHistoryAsync(Guid id, CancellationToken cancellationToken)
    {
        await GetAccessibleAsync(id, cancellationToken);
        return await bookings.GetHistoryAsync(tenantContext.TenantId, id, cancellationToken);
    }

    public async Task<IdempotentResult<BookingDto>> CreateAsync(CreateBookingRequest request, IdempotencyRequest idempotencyRequest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await createValidator.EnsureValidAsync(request, cancellationToken);
        var venueRef = await guard.EnsureAccessAsync(request.VenueId, cancellationToken);
        var tenantId = tenantContext.TenantId;
        var userId = currentUser.RequireUserId();
        var scope = $"booking.create:user:{userId}";
        var venue = await LoadActiveVenueAsync(tenantId, request.VenueId, cancellationToken);
        var schedule = await venueService.LoadScheduleAsync(venueRef, request.Date, request.Date, cancellationToken);
        var timing = BookingAllocator.ResolveTiming(schedule, venue.Rules, request.Date, request.Time, request.DurationMinutes);
        EnsureNotInPast(timing);

        var source = request.Source ?? BookingSource.Staff;
        var status = request.Status ?? (source == BookingSource.WalkIn ? BookingStatus.Seated : BookingStatus.Confirmed);
        var creation = new BookingCreation(tenantId, venue, timing, request, source, status, HistorySources.Staff, userId, scope);
        return await CreateCoreAsync(creation, idempotencyRequest, ToDto, dto => dto, cancellationToken);
    }

    /// <summary>
    /// The create transaction shared by staff and public bookings: idempotency claim, customer, booking row, allocation
    /// (the exclusion constraint decides), history, event, audit and outbox — committed together or not at all.
    /// </summary>
    internal async Task<IdempotentResult<TResult>> CreateCoreAsync<TResult>(
        BookingCreation creation,
        IdempotencyRequest idempotencyRequest,
        Func<BookingRecord, TResult> map,
        Func<TResult, object> storedResponse,
        CancellationToken cancellationToken)
        where TResult : class
    {
        var (tenantId, venue, timing, request, source, status, historySource, userId, scope) = creation;
        var bookingId = creation.BookingId;

        return await TransientRetry.ExecuteAsync(
            () => unitOfWork.ExecuteAsync(async () =>
            {
                var replay = await idempotency.BeginAsync(tenantId, scope, idempotencyRequest, cancellationToken);
                if (replay is not null)
                {
                    return new IdempotentResult<TResult>(null, replay);
                }

                var now = clock.UtcNow;
                var customerId = await ResolveCustomerAsync(tenantId, request.Customer, creation.MarketingConsent, cancellationToken);
                if (creation.MaxActiveBookingsPerCustomer is { } maxActive && customerId is { } existingCustomer
                    && await bookings.CountActiveUpcomingAsync(tenantId, venue.Id, existingCustomer, now, cancellationToken) >= maxActive)
                {
                    throw new BusinessRuleAppException(ErrorCodes.ContactBookingLimit,
                        "There are already several upcoming bookings for these contact details. Contact the venue to book more.");
                }

                var hold = request.HoldToken is { } token
                    ? await LockHoldForConversionAsync(tenantId, venue.Id, token, timing, now, cancellationToken)
                    : null;

                var reference = BookingReference.Format(timing.BusinessDate, await bookings.NextReferenceSequenceAsync(cancellationToken));
                await bookings.InsertAsync(tenantId, new NewBooking(bookingId, venue.Id, customerId, reference, status, source, request.PartySize,
                    timing.StartUtc, timing.EndUtc, timing.BusinessDateOnly, Clean(request.SpecialRequests), Clean(request.InternalNotes), userId, now)
                {
                    PublicTokenHash = creation.PublicTokenHash,
                    ExpiresAt = creation.Deposit?.ExpiresAt,
                }, cancellationToken);
                if (creation.Deposit is { } deposit)
                {
                    await payments.InsertAsync(new NewPayment(Guid.CreateVersion7(), tenantId, venue.Id, bookingId, PaymentPurposes.Deposit,
                        deposit.Provider, deposit.AmountMinor, deposit.Currency, deposit.ExpiresAt, now), cancellationToken);
                }

                IReadOnlyList<AllocationCandidate> candidates;
                if (hold is not null)
                {
                    var heldResources = await allocations.ListActiveResourceIdsAsync(tenantId, null, hold.Id, null, cancellationToken);
                    candidates = [await allocator.PlanResourcesAsync(tenantId, venue.Id, heldResources, request.PartySize, cancellationToken)];
                    await allocations.DeactivateAsync(tenantId, null, hold.Id, null, ReleaseReasons.HoldConverted, now, cancellationToken);
                }
                else if (creation.OnlineOnly)
                {
                    candidates = await allocator.PlanOnlineAsync(tenantId, venue.Id, request.PartySize, timing, cancellationToken);
                }
                else
                {
                    candidates = await allocator.PlanAsync(tenantId, venue.Id, request.Allocation, request.PartySize, timing, null, cancellationToken);
                }

                var chosen = await allocator.AllocateAsync(tenantId, candidates,
                    c => new AllocationInsert(tenantId, venue.Id, AllocationTypes.Booking, bookingId, null, null, c.ResourceIds, timing.StartUtc, timing.ClaimEndUtc, null),
                    cancellationToken);
                await bookings.ReplaceResourcesAsync(tenantId, venue.Id, bookingId, chosen.ResourceIds, chosen.CombinationId, cancellationToken);
                if (hold is not null)
                {
                    await holds.SetStatusAsync(tenantId, hold.Id, "CONVERTED", bookingId, now, cancellationToken);
                }

                await bookings.WriteStatusHistoryAsync(new StatusHistoryEntry(tenantId, venue.Id, bookingId, null, status, historySource, null, userId), cancellationToken);
                await bookings.WriteEventAsync(new BookingEventEntry(tenantId, venue.Id, bookingId, BookingEventTypes.Created, new
                {
                    reference,
                    status,
                    source,
                    partySize = request.PartySize,
                    startsAt = timing.StartUtc,
                    endsAt = timing.EndUtc,
                    resourceIds = chosen.ResourceIds,
                    combinationId = chosen.CombinationId,
                    holdId = hold?.Id,
                }, userId), cancellationToken);

                var created = await LoadRecordAsync(tenantId, bookingId, cancellationToken);
                await audit.WriteAsync(new AuditEntry(tenantId, EntityType, bookingId, AuditActions.Created, NewValues: Snapshot(created)), cancellationToken);
                await outbox.WriteRealtimeAsync(tenantId, RealtimeEvents.BookingCreated, venue.Id, bookingId, created.Version, created.BusinessDate, now, cancellationToken);
                if (created.CustomerId is not null && status is BookingStatus.Confirmed or BookingStatus.Pending)
                {
                    await outbox.WriteNotificationAsync(tenantId, NotificationTypes.BookingConfirmation, venue.Id, bookingId, now, cancellationToken);
                }

                var result = map(created);
                await idempotency.CompleteAsync(tenantId, scope, idempotencyRequest, 201, storedResponse(result), bookingId, cancellationToken);
                return new IdempotentResult<TResult>(result, null);
            }, cancellationToken),
            () => BookingAllocator.NotAvailable([]));
    }

    public async Task<BookingDto> UpdateAsync(Guid id, UpdateBookingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await updateValidator.EnsureValidAsync(request, cancellationToken);
        await GetAccessibleAsync(id, cancellationToken);
        var tenantId = tenantContext.TenantId;
        var userId = currentUser.UserId;

        return await TransientRetry.ExecuteAsync(
            () => unitOfWork.ExecuteAsync(async () =>
            {
                var before = await LockMutableAsync(tenantId, id, request.Version, cancellationToken);
                if (request.CustomerId is { } customerId && customerId != before.CustomerId
                    && await customers.GetAsync(tenantId, customerId, cancellationToken) is null)
                {
                    throw new NotFoundAppException(ErrorCodes.CustomerNotFound, "The customer was not found.");
                }

                if (request.PartySize != before.PartySize)
                {
                    var current = await allocations.ListActiveResourceIdsAsync(tenantId, id, null, null, cancellationToken);
                    await allocator.PlanResourcesAsync(tenantId, before.VenueId, current, request.PartySize, cancellationToken);
                }

                var now = clock.UtcNow;
                var specialRequests = Clean(request.SpecialRequests);
                var internalNotes = Clean(request.InternalNotes);
                await bookings.UpdateDetailsAsync(tenantId, id, request.PartySize, specialRequests, internalNotes, request.CustomerId ?? before.CustomerId, now, cancellationToken);

                var changed = new (string Field, bool Changed)[]
                    {
                        ("partySize", request.PartySize != before.PartySize),
                        ("specialRequests", specialRequests != before.SpecialRequests),
                        ("internalNotes", internalNotes != before.InternalNotes),
                        ("customerId", request.CustomerId is not null && request.CustomerId != before.CustomerId),
                    }
                    .Where(c => c.Changed)
                    .Select(c => c.Field)
                    .ToList();

                await bookings.WriteEventAsync(new BookingEventEntry(tenantId, before.VenueId, id, BookingEventTypes.Updated, new
                {
                    changed,
                    partySize = new { from = before.PartySize, to = request.PartySize },
                }, userId), cancellationToken);

                var after = await LoadRecordAsync(tenantId, id, cancellationToken);
                await audit.WriteAsync(new AuditEntry(tenantId, EntityType, id, AuditActions.Updated, Snapshot(before), Snapshot(after)), cancellationToken);
                await outbox.WriteRealtimeAsync(tenantId, RealtimeEvents.BookingUpdated, after.VenueId, id, after.Version, after.BusinessDate, now, cancellationToken);
                return ToDto(after);
            }, cancellationToken),
            ConcurrencyConflict);
    }

    /// <summary>Atomic move: old allocations are released and new ones claimed in one transaction, so a conflict leaves the booking intact.</summary>
    public async Task<BookingDto> MoveAsync(Guid id, MoveBookingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await moveValidator.EnsureValidAsync(request, cancellationToken);
        var existing = await GetAccessibleAsync(id, cancellationToken);
        var tenantId = tenantContext.TenantId;
        var userId = currentUser.UserId;
        var venueRef = await guard.EnsureAccessAsync(existing.VenueId, cancellationToken);
        var venue = await LoadActiveVenueAsync(tenantId, existing.VenueId, cancellationToken);

        var zone = TimeZones.Get(existing.TimeZone);
        var date = request.Date ?? existing.BusinessDate;
        var time = request.Time ?? LocalTimeOf(existing.StartsAt, zone);
        var duration = request.DurationMinutes ?? (int)(existing.EndsAt - existing.StartsAt).TotalMinutes;
        var schedule = await venueService.LoadScheduleAsync(venueRef, date, date, cancellationToken);
        var timing = BookingAllocator.ResolveTiming(schedule, venue.Rules, date, time, duration);
        EnsureNotInPast(timing);

        return await TransientRetry.ExecuteAsync(
            () => unitOfWork.ExecuteAsync(async () =>
            {
                var before = await LockMutableAsync(tenantId, id, request.Version, cancellationToken);
                var now = clock.UtcNow;
                var oldResources = await allocations.ListActiveResourceIdsAsync(tenantId, id, null, null, cancellationToken);
                var allocation = request.Allocation ?? CurrentAllocation(before, oldResources);

                await allocations.DeactivateAsync(tenantId, id, null, null, ReleaseReasons.Moved, now, cancellationToken);
                var candidates = await allocator.PlanAsync(tenantId, before.VenueId, allocation, before.PartySize, timing, null, cancellationToken);
                var chosen = await allocator.AllocateAsync(tenantId, candidates,
                    c => new AllocationInsert(tenantId, before.VenueId, AllocationTypes.Booking, id, null, null, c.ResourceIds, timing.StartUtc, timing.ClaimEndUtc, null),
                    cancellationToken);

                await bookings.ReplaceResourcesAsync(tenantId, before.VenueId, id, chosen.ResourceIds, chosen.CombinationId, cancellationToken);
                await bookings.UpdateTimesAsync(tenantId, id, timing.StartUtc, timing.EndUtc, timing.BusinessDateOnly, now, cancellationToken);
                await bookings.WriteEventAsync(new BookingEventEntry(tenantId, before.VenueId, id, BookingEventTypes.Moved, new
                {
                    from = new { startsAt = before.StartsAt, endsAt = before.EndsAt, businessDate = before.BusinessDate, resourceIds = oldResources },
                    to = new { startsAt = timing.StartUtc, endsAt = timing.EndUtc, businessDate = timing.BusinessDateOnly, resourceIds = chosen.ResourceIds, combinationId = chosen.CombinationId },
                }, userId), cancellationToken);

                var after = await LoadRecordAsync(tenantId, id, cancellationToken);
                await audit.WriteAsync(new AuditEntry(tenantId, EntityType, id, AuditActions.Moved, Snapshot(before), Snapshot(after)), cancellationToken);
                await outbox.WriteRealtimeAsync(tenantId, RealtimeEvents.BookingMoved, after.VenueId, id, after.Version, after.BusinessDate, now, cancellationToken);
                return ToDto(after);
            }, cancellationToken),
            () => BookingAllocator.NotAvailable([]));
    }

    public Task<BookingDto> ConfirmAsync(Guid id, BookingCommandRequest request, CancellationToken cancellationToken) =>
        TransitionAsync(id, BookingCommand.Confirm, null, request?.Version, cancellationToken);

    public Task<BookingDto> CheckInAsync(Guid id, BookingCommandRequest request, CancellationToken cancellationToken) =>
        TransitionAsync(id, BookingCommand.CheckIn, null, request?.Version, cancellationToken);

    public Task<BookingDto> SeatAsync(Guid id, BookingCommandRequest request, CancellationToken cancellationToken) =>
        TransitionAsync(id, BookingCommand.Seat, null, request?.Version, cancellationToken);

    public Task<BookingDto> CompleteAsync(Guid id, BookingCommandRequest request, CancellationToken cancellationToken) =>
        TransitionAsync(id, BookingCommand.Complete, null, request?.Version, cancellationToken);

    public Task<BookingDto> NoShowAsync(Guid id, BookingCommandRequest request, CancellationToken cancellationToken) =>
        TransitionAsync(id, BookingCommand.NoShow, null, request?.Version, cancellationToken);

    public async Task<BookingDto> CancelAsync(Guid id, CancelBookingRequest request, CancellationToken cancellationToken)
    {
        request ??= new CancelBookingRequest();
        await cancelValidator.EnsureValidAsync(request, cancellationToken);
        return await TransitionAsync(id, BookingCommand.Cancel, Clean(request.Reason), request.Version, cancellationToken);
    }

    private async Task<BookingDto> TransitionAsync(Guid id, BookingCommand command, string? reason, int? version, CancellationToken cancellationToken)
    {
        await GetAccessibleAsync(id, cancellationToken);
        var after = await TransitionCoreAsync(tenantContext.TenantId, id, command, reason, version, currentUser.UserId, HistorySources.Staff, null, cancellationToken);
        return ToDto(after);
    }

    /// <summary>
    /// Applies a status command under a row lock. <paramref name="precondition"/> runs against the locked booking before
    /// anything changes (public cancellation uses it for the venue's cancellation policy).
    /// </summary>
    internal Task<BookingRecord> TransitionCoreAsync(
        Guid tenantId,
        Guid id,
        BookingCommand command,
        string? reason,
        int? version,
        Guid? userId,
        string historySource,
        Action<BookingRecord>? precondition,
        CancellationToken cancellationToken) =>
        transitions.ApplyAsync(tenantId, id, command, new TransitionContext(historySource, userId, reason, version) { Precondition = precondition }, cancellationToken);

    private async Task<BookingRecord> LockMutableAsync(Guid tenantId, Guid id, int? version, CancellationToken cancellationToken)
    {
        var locked = await bookings.LockAsync(tenantId, id, cancellationToken)
            ?? throw new NotFoundAppException(ErrorCodes.BookingNotFound, NotFoundMessage);
        EnsureVersion(version, locked.Version);
        if (!BookingStatusMachine.IsMutable(locked.Status))
        {
            throw new ConflictAppException(ErrorCodes.InvalidStatusTransition, $"A {Describe(locked.Status)} booking cannot be changed.");
        }

        return await LoadRecordAsync(tenantId, id, cancellationToken);
    }

    private async Task<HoldRecord> LockHoldForConversionAsync(Guid tenantId, Guid venueId, string token, BookingTiming timing, DateTime now, CancellationToken cancellationToken)
    {
        var hold = await holds.LockByTokenAsync(tenantId, HoldTokens.Hash(token), cancellationToken);
        if (hold is null || hold.VenueId != venueId || hold.Status is "RELEASED" or "CONVERTED")
        {
            throw new NotFoundAppException(ErrorCodes.HoldNotFound, "The hold was not found.");
        }

        if (hold.Status == "EXPIRED" || hold.ExpiresAt <= now)
        {
            throw new GoneAppException(ErrorCodes.HoldExpired, "The hold has expired. Search availability again.");
        }

        if (hold.StartsAt != timing.StartUtc || hold.EndsAt != timing.EndUtc)
        {
            throw new ValidationAppException("holdToken", "The hold is for a different time or duration.");
        }

        return hold;
    }

    /// <summary>An existing customer with the same email is reused unchanged; <paramref name="marketingConsent"/> only applies to new customers.</summary>
    private async Task<Guid?> ResolveCustomerAsync(Guid tenantId, BookingCustomerInput? input, bool marketingConsent, CancellationToken cancellationToken)
    {
        if (input is null)
        {
            return null;
        }

        if (input.CustomerId is { } customerId)
        {
            return (await customers.GetAsync(tenantId, customerId, cancellationToken))?.Id
                ?? throw new NotFoundAppException(ErrorCodes.CustomerNotFound, "The customer was not found.");
        }

        var email = Clean(input.Email)?.ToLowerInvariant();
        if (email is not null && await customers.FindByEmailAsync(tenantId, email, cancellationToken) is { } existing)
        {
            return existing;
        }

        var phone = Clean(input.Phone);
        return await customers.CreateAsync(tenantId,
            new NewCustomer(input.FirstName!.Trim(), Clean(input.LastName), email, phone, NormalizePhone(phone), marketingConsent), cancellationToken);
    }

    private async Task<VenueDto> LoadActiveVenueAsync(Guid tenantId, Guid venueId, CancellationToken cancellationToken)
    {
        var venue = await venues.GetAsync(tenantId, venueId, cancellationToken)
            ?? throw new NotFoundAppException(ErrorCodes.VenueNotFound, "The venue was not found.");
        if (venue.Status != VenueStatus.Active)
        {
            throw new BusinessRuleAppException(ErrorCodes.BusinessRuleViolation, "The venue is not accepting bookings.");
        }

        return venue;
    }

    private void EnsureNotInPast(BookingTiming timing)
    {
        if (timing.EndUtc <= clock.UtcNow)
        {
            throw new BusinessRuleAppException(ErrorCodes.InvalidBookingTime, "The booking would end in the past.");
        }
    }

    private async Task<BookingRecord> GetAccessibleAsync(Guid id, CancellationToken cancellationToken)
    {
        var record = await bookings.GetAsync(tenantContext.TenantId, id, cancellationToken)
            ?? throw new NotFoundAppException(ErrorCodes.BookingNotFound, NotFoundMessage);
        try
        {
            await guard.EnsureAccessAsync(record.VenueId, cancellationToken);
        }
        catch (NotFoundAppException)
        {
            throw new NotFoundAppException(ErrorCodes.BookingNotFound, NotFoundMessage);
        }

        return record;
    }

    private async Task<BookingRecord> LoadRecordAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        await bookings.GetAsync(tenantId, id, cancellationToken)
        ?? throw new NotFoundAppException(ErrorCodes.BookingNotFound, NotFoundMessage);

    private static AllocationRequest CurrentAllocation(BookingRecord booking, IReadOnlyList<Guid> activeResources)
    {
        var combinationId = booking.Resources.Select(r => r.CombinationId).FirstOrDefault(c => c is not null);
        if (combinationId is not null)
        {
            return new AllocationRequest(AllocationModes.Combination, CombinationId: combinationId);
        }

        var resourceIds = activeResources.Count > 0 ? activeResources : booking.Resources.Select(r => r.ResourceId).ToList();
        return new AllocationRequest(AllocationModes.Resources, resourceIds);
    }

    private static void EnsureVersion(int? expected, int actual) => BookingTransitions.EnsureVersion(expected, actual);

    private static Exception ConcurrencyConflict() => BookingTransitions.ConcurrencyConflict();

    public static BookingDto ToDto(BookingRecord r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var zone = TimeZones.Get(r.TimeZone);
        return new BookingDto(r.Id, r.VenueId, r.Reference, r.Status, r.Source, r.PartySize, r.StartsAt, r.EndsAt, r.BusinessDate,
            LocalTimeOf(r.StartsAt, zone), (int)(r.EndsAt - r.StartsAt).TotalMinutes, r.TimeZone, r.Customer, r.Resources,
            r.SpecialRequests, r.InternalNotes, r.ExpiresAt, r.CreatedByUserId, r.Version, r.ConfirmedAt, r.CheckedInAt, r.SeatedAt,
            r.CompletedAt, r.CancelledAt, r.NoShowAt, r.CancellationReason, r.CreatedAt, r.UpdatedAt);
    }

    /// <summary>Audit values: booking facts and ids only (no customer contact details or notes).</summary>
    internal static object Snapshot(BookingRecord r) => new
    {
        r.Reference,
        r.Status,
        r.Source,
        r.PartySize,
        r.StartsAt,
        r.EndsAt,
        r.BusinessDate,
        r.CustomerId,
        ResourceIds = r.Resources.Select(x => x.ResourceId).ToList(),
        r.Version,
    };

    private static TimeOnly LocalTimeOf(DateTime utc, DateTimeZone zone) =>
        Instant.FromDateTimeUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).InZone(zone).TimeOfDay.ToTimeOnly();

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Digits with an optional leading +; null when the result is not a plausible phone number.</summary>
    public static string? NormalizePhone(string? phone)
    {
        if (phone is null)
        {
            return null;
        }

        var trimmed = phone.Trim();
        var digits = NonDigits().Replace(trimmed, string.Empty);
        var normalized = trimmed.StartsWith('+') ? "+" + digits : digits;
        return PhonePattern().IsMatch(normalized) ? normalized : null;
    }

    private static string Describe(string status) => BookingTransitions.Describe(status);

    [GeneratedRegex("[^0-9]")]
    private static partial Regex NonDigits();

    [GeneratedRegex(@"^\+?[0-9]{4,20}$")]
    private static partial Regex PhonePattern();
}
