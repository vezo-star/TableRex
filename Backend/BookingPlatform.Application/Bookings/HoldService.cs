using BookingPlatform.Application.Common.Auditing;
using BookingPlatform.Application.Common.Errors;
using BookingPlatform.Application.Common.Idempotency;
using BookingPlatform.Application.Common.Outbox;
using BookingPlatform.Application.Common.Persistence;
using BookingPlatform.Application.Common.Security;
using BookingPlatform.Application.Common.Validation;
using BookingPlatform.Application.Venues;
using BookingPlatform.Domain.Venues;
using FluentValidation;
using IClock = BookingPlatform.Application.Common.Time.IClock;

namespace BookingPlatform.Application.Bookings;

/// <summary>
/// Short-lived provisional claims (default 5 minutes). The token is returned once and converts the hold into a booking
/// (<c>holdToken</c> on create). Expired holds stop blocking immediately (read-time filter + lazy release).
/// </summary>
public sealed class HoldService(
    IHoldRepository holds,
    IAllocationRepository allocations,
    BookingAllocator allocator,
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
    IValidator<CreateHoldRequest> validator)
{
    private const string EntityType = "resource_hold";
    private const string NotFoundMessage = "The hold was not found.";

    public async Task<IdempotentResult<HoldDto>> CreateAsync(CreateHoldRequest request, IdempotencyRequest idempotencyRequest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await validator.EnsureValidAsync(request, cancellationToken);
        var venueRef = await guard.EnsureAccessAsync(request.VenueId, cancellationToken);
        var tenantId = tenantContext.TenantId;
        var userId = currentUser.RequireUserId();
        var scope = $"hold.create:user:{userId}";
        var venue = await venues.GetAsync(tenantId, request.VenueId, cancellationToken)
            ?? throw new NotFoundAppException(ErrorCodes.VenueNotFound, "The venue was not found.");
        if (venue.Status != VenueStatus.Active)
        {
            throw new BusinessRuleAppException(ErrorCodes.BusinessRuleViolation, "The venue is not accepting bookings.");
        }

        var schedule = await venueService.LoadScheduleAsync(venueRef, request.Date, request.Date, cancellationToken);
        var timing = BookingAllocator.ResolveTiming(schedule, venue.Rules, request.Date, request.Time, request.DurationMinutes);
        if (timing.EndUtc <= clock.UtcNow)
        {
            throw new BusinessRuleAppException(ErrorCodes.InvalidBookingTime, "The hold would end in the past.");
        }

        return await TransientRetry.ExecuteAsync(
            () => unitOfWork.ExecuteAsync(async () =>
            {
                var replay = await idempotency.BeginAsync(tenantId, scope, idempotencyRequest, cancellationToken);
                if (replay is not null)
                {
                    return new IdempotentResult<HoldDto>(null, replay);
                }

                var now = clock.UtcNow;
                var expiresAt = now.AddSeconds(venue.Rules.HoldTtlSeconds);
                var holdId = Guid.CreateVersion7();
                var (token, tokenHash) = HoldTokens.Generate();
                await holds.InsertAsync(tenantId, holdId, venue.Id, "USER", userId.ToString(), tokenHash, request.PartySize,
                    timing.StartUtc, timing.EndUtc, expiresAt, userId, cancellationToken);

                var candidates = await allocator.PlanAsync(tenantId, venue.Id, request.Allocation, request.PartySize, timing, null, cancellationToken);
                var chosen = await allocator.AllocateAsync(tenantId, candidates,
                    c => new AllocationInsert(tenantId, venue.Id, AllocationTypes.Hold, null, holdId, null, c.ResourceIds, timing.StartUtc, timing.ClaimEndUtc, expiresAt),
                    cancellationToken);

                var dto = new HoldDto(holdId, venue.Id, "ACTIVE", request.PartySize, timing.StartUtc, timing.EndUtc, expiresAt, chosen.ResourceIds, token);
                var stored = dto with { Token = null };
                await audit.WriteAsync(new AuditEntry(tenantId, EntityType, holdId, AuditActions.Created, NewValues: stored), cancellationToken);
                await outbox.WriteRealtimeAsync(tenantId, RealtimeEvents.HoldCreated, venue.Id, holdId, null, timing.BusinessDateOnly, now, cancellationToken);
                await idempotency.CompleteAsync(tenantId, scope, idempotencyRequest, 201, stored, holdId, cancellationToken);
                return new IdempotentResult<HoldDto>(dto, null);
            }, cancellationToken),
            () => BookingAllocator.NotAvailable([]));
    }

    /// <summary>Releases an active hold. Releasing a hold that is no longer active is a no-op.</summary>
    public async Task ReleaseAsync(Guid holdId, CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId;
        var hold = await holds.GetAsync(tenantId, holdId, cancellationToken)
            ?? throw new NotFoundAppException(ErrorCodes.HoldNotFound, NotFoundMessage);
        try
        {
            await guard.EnsureAccessAsync(hold.VenueId, cancellationToken);
        }
        catch (NotFoundAppException)
        {
            throw new NotFoundAppException(ErrorCodes.HoldNotFound, NotFoundMessage);
        }

        await unitOfWork.ExecuteAsync(async () =>
        {
            var locked = await holds.LockAsync(tenantId, holdId, cancellationToken);
            if (locked is not { Status: "ACTIVE" })
            {
                return;
            }

            var now = clock.UtcNow;
            await allocations.DeactivateAsync(tenantId, null, holdId, null, ReleaseReasons.HoldReleased, now, cancellationToken);
            await holds.SetStatusAsync(tenantId, holdId, "RELEASED", null, now, cancellationToken);
            await audit.WriteAsync(new AuditEntry(tenantId, EntityType, holdId, AuditActions.Deactivated,
                new { locked.Status, locked.StartsAt, locked.EndsAt }, new { Status = "RELEASED" }), cancellationToken);
            await outbox.WriteRealtimeAsync(tenantId, RealtimeEvents.HoldExpired, locked.VenueId, holdId, null, null, now, cancellationToken);
        }, cancellationToken);
    }
}
