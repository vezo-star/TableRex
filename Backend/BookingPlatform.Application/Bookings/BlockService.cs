using BookingPlatform.Application.Common.Auditing;
using BookingPlatform.Application.Common.Errors;
using BookingPlatform.Application.Common.Outbox;
using BookingPlatform.Application.Common.Persistence;
using BookingPlatform.Application.Common.Security;
using BookingPlatform.Application.Common.Validation;
using BookingPlatform.Application.Venues;
using FluentValidation;
using IClock = BookingPlatform.Application.Common.Time.IClock;

namespace BookingPlatform.Application.Bookings;

/// <summary>Staff-created unavailability. Blocks claim resources through BLOCK allocations, so they cannot overlap bookings.</summary>
public sealed class BlockService(
    IBlockRepository blocks,
    IAllocationRepository allocations,
    IAllocatableResourceRepository resources,
    BookingAllocator allocator,
    IVenueAccessGuard guard,
    IAuditWriter audit,
    IOutboxWriter outbox,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IClock clock,
    IValidator<CreateBlockRequest> validator)
{
    private const string EntityType = "resource_block";
    private const string NotFoundMessage = "The block was not found.";

    public async Task<IReadOnlyList<BlockDto>> ListAsync(Guid venueId, DateTime? from, DateTime? to, bool includeCancelled, CancellationToken cancellationToken)
    {
        await guard.EnsureAccessAsync(venueId, cancellationToken);
        return await blocks.ListAsync(tenantContext.TenantId, venueId, from, to, includeCancelled, cancellationToken);
    }

    public Task<BlockDto> GetAsync(Guid id, CancellationToken cancellationToken) => GetAccessibleAsync(id, cancellationToken);

    public async Task<BlockDto> CreateAsync(CreateBlockRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await validator.EnsureValidAsync(request, cancellationToken);
        await guard.EnsureAccessAsync(request.VenueId, cancellationToken);
        var tenantId = tenantContext.TenantId;
        var userId = currentUser.UserId;
        var normalised = request with
        {
            StartsAt = DateTime.SpecifyKind(request.StartsAt.ToUniversalTime(), DateTimeKind.Utc),
            EndsAt = DateTime.SpecifyKind(request.EndsAt.ToUniversalTime(), DateTimeKind.Utc),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
        };

        var found = await resources.GetManyAsync(tenantId, normalised.ResourceIds, cancellationToken);
        if (found.Count != normalised.ResourceIds.Count || found.Any(r => r.VenueId != normalised.VenueId))
        {
            throw new NotFoundAppException(ErrorCodes.ResourceNotFound, "One or more resources were not found.");
        }

        return await TransientRetry.ExecuteAsync(
            () => unitOfWork.ExecuteAsync(async () =>
            {
                var blockId = Guid.CreateVersion7();
                await blocks.InsertAsync(tenantId, blockId, normalised, userId, cancellationToken);
                var candidate = new AllocationCandidate(normalised.ResourceIds.Order().ToList(), null);
                await allocator.AllocateAsync(tenantId, [candidate],
                    c => new AllocationInsert(tenantId, normalised.VenueId, AllocationTypes.Block, null, null, blockId, c.ResourceIds,
                        normalised.StartsAt, normalised.EndsAt, null),
                    cancellationToken);

                var created = (await blocks.GetAsync(tenantId, blockId, cancellationToken))!;
                await audit.WriteAsync(new AuditEntry(tenantId, EntityType, blockId, AuditActions.Created, NewValues: created), cancellationToken);
                await outbox.WriteRealtimeAsync(tenantId, RealtimeEvents.ResourceBlocked, normalised.VenueId, blockId, null, null, clock.UtcNow, cancellationToken);
                return created;
            }, cancellationToken),
            () => BookingAllocator.NotAvailable([]));
    }

    /// <summary>Cancels the block and releases its allocations (history is kept). Cancelling twice is a no-op.</summary>
    public async Task<BlockDto> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        var existing = await GetAccessibleAsync(id, cancellationToken);
        var tenantId = tenantContext.TenantId;
        var userId = currentUser.UserId;

        return await unitOfWork.ExecuteAsync(async () =>
        {
            var now = clock.UtcNow;
            if (await blocks.CancelAsync(tenantId, id, userId, now, cancellationToken))
            {
                await allocations.DeactivateAsync(tenantId, null, null, id, ReleaseReasons.BlockCancelled, now, cancellationToken);
                var after = (await blocks.GetAsync(tenantId, id, cancellationToken))!;
                await audit.WriteAsync(new AuditEntry(tenantId, EntityType, id, AuditActions.Cancelled, existing, after), cancellationToken);
                await outbox.WriteRealtimeAsync(tenantId, RealtimeEvents.ResourceBlocked, existing.VenueId, id, null, null, now, cancellationToken);
                return after;
            }

            return (await blocks.GetAsync(tenantId, id, cancellationToken))!;
        }, cancellationToken);
    }

    private async Task<BlockDto> GetAccessibleAsync(Guid id, CancellationToken cancellationToken)
    {
        var block = await blocks.GetAsync(tenantContext.TenantId, id, cancellationToken)
            ?? throw new NotFoundAppException(ErrorCodes.BlockNotFound, NotFoundMessage);
        try
        {
            await guard.EnsureAccessAsync(block.VenueId, cancellationToken);
        }
        catch (NotFoundAppException)
        {
            throw new NotFoundAppException(ErrorCodes.BlockNotFound, NotFoundMessage);
        }

        return block;
    }
}
