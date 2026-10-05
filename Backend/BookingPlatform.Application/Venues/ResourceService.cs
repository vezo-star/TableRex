using BookingPlatform.Application.Common.Auditing;
using BookingPlatform.Application.Common.Errors;
using BookingPlatform.Application.Common.Outbox;
using BookingPlatform.Application.Common.Pagination;
using BookingPlatform.Application.Common.Persistence;
using BookingPlatform.Application.Common.Security;
using BookingPlatform.Application.Common.Time;
using BookingPlatform.Application.Common.Validation;
using BookingPlatform.Application.Subscriptions;
using FluentValidation;

namespace BookingPlatform.Application.Venues;

public sealed class ResourceService(
    IResourceRepository resources,
    IVenueRepository venues,
    IVenueAccessGuard guard,
    IAuditWriter audit,
    IOutboxWriter outbox,
    IClock clock,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    PlanLimits planLimits,
    IValidator<CreateResourceTypeRequest> createTypeValidator,
    IValidator<UpdateResourceTypeRequest> updateTypeValidator,
    IValidator<CreateResourceRequest> createValidator,
    IValidator<UpdateResourceRequest> updateValidator,
    IValidator<ResourceQuery> queryValidator)
{
    private const string NotFoundMessage = "The resource was not found.";

    public Task<IReadOnlyList<ResourceTypeDto>> ListTypesAsync(CancellationToken cancellationToken) =>
        resources.ListTypesAsync(tenantContext.TenantId, cancellationToken);

    public async Task<ResourceTypeDto> CreateTypeAsync(CreateResourceTypeRequest request, CancellationToken cancellationToken)
    {
        await createTypeValidator.EnsureValidAsync(request, cancellationToken);
        var tenantId = tenantContext.TenantId;
        var normalised = request with { Name = request.Name.Trim() };

        return await unitOfWork.ExecuteAsync(async () =>
        {
            if (await resources.TypeCodeExistsAsync(tenantId, normalised.Code, cancellationToken))
            {
                throw new ConflictAppException(ErrorCodes.CodeTaken, "A resource type with that code already exists.");
            }

            var id = await resources.CreateTypeAsync(tenantId, normalised, cancellationToken);
            var created = (await resources.GetTypeAsync(tenantId, id, cancellationToken))!;
            await audit.WriteAsync(new AuditEntry(tenantId, "resource_type", id, AuditActions.Created, NewValues: created), cancellationToken);
            return created;
        }, cancellationToken);
    }

    public async Task<ResourceTypeDto> UpdateTypeAsync(Guid id, UpdateResourceTypeRequest request, CancellationToken cancellationToken)
    {
        await updateTypeValidator.EnsureValidAsync(request, cancellationToken);
        var tenantId = tenantContext.TenantId;

        return await unitOfWork.ExecuteAsync(async () =>
        {
            var before = await resources.GetTypeAsync(tenantId, id, cancellationToken)
                ?? throw new NotFoundAppException(ErrorCodes.ResourceTypeNotFound, "The resource type was not found.");
            await resources.UpdateTypeAsync(tenantId, id, request with { Name = request.Name.Trim() }, cancellationToken);
            var after = (await resources.GetTypeAsync(tenantId, id, cancellationToken))!;
            await audit.WriteAsync(new AuditEntry(tenantId, "resource_type", id, AuditActions.Updated, before, after), cancellationToken);
            return after;
        }, cancellationToken);
    }

    public async Task<PagedResult<ResourceDto>> ListAsync(ResourceQuery query, CancellationToken cancellationToken)
    {
        await queryValidator.EnsureValidAsync(query, cancellationToken);
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

        return await resources.ListAsync(tenantContext.TenantId, query, venueIds, cancellationToken);
    }

    public async Task<ResourceDto> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await GetAccessibleAsync(id, cancellationToken);

    public async Task<ResourceDto> CreateAsync(CreateResourceRequest request, CancellationToken cancellationToken)
    {
        await createValidator.EnsureValidAsync(request, cancellationToken);
        await guard.EnsureAccessAsync(request.VenueId, cancellationToken);
        var tenantId = tenantContext.TenantId;
        var normalised = request with { Name = request.Name.Trim(), Code = request.Code.Trim() };

        return await unitOfWork.ExecuteAsync(async () =>
        {
            await EnsureReferencesAsync(tenantId, request.VenueId, request.SectionId, request.ResourceTypeId, cancellationToken);
            await planLimits.EnsureCanAddResourceAsync(tenantId, request.VenueId, cancellationToken);
            if (await resources.CodeExistsAsync(tenantId, request.VenueId, normalised.Code, null, cancellationToken))
            {
                throw new ConflictAppException(ErrorCodes.CodeTaken, "Another resource in this venue already uses that code.");
            }

            var id = await resources.CreateAsync(tenantId, normalised, cancellationToken);
            var created = (await resources.GetAsync(tenantId, id, cancellationToken))!;
            await audit.WriteAsync(new AuditEntry(tenantId, "bookable_resource", id, AuditActions.Created, NewValues: created), cancellationToken);
            await PublishAsync(tenantId, created.VenueId, id, cancellationToken);
            return created;
        }, cancellationToken);
    }

    public async Task<ResourceDto> UpdateAsync(Guid id, UpdateResourceRequest request, CancellationToken cancellationToken)
    {
        await updateValidator.EnsureValidAsync(request, cancellationToken);
        var existing = await GetAccessibleAsync(id, cancellationToken);
        var tenantId = tenantContext.TenantId;
        var normalised = request with { Name = request.Name.Trim(), Code = request.Code.Trim() };

        return await unitOfWork.ExecuteAsync(async () =>
        {
            await EnsureReferencesAsync(tenantId, existing.VenueId, request.SectionId, request.ResourceTypeId, cancellationToken);
            if (!existing.IsActive && request.IsActive)
            {
                await planLimits.EnsureCanAddResourceAsync(tenantId, existing.VenueId, cancellationToken);
            }

            if (await resources.CodeExistsAsync(tenantId, existing.VenueId, normalised.Code, id, cancellationToken))
            {
                throw new ConflictAppException(ErrorCodes.CodeTaken, "Another resource in this venue already uses that code.");
            }

            await resources.UpdateAsync(tenantId, id, normalised, cancellationToken);
            var after = (await resources.GetAsync(tenantId, id, cancellationToken))!;
            await audit.WriteAsync(new AuditEntry(tenantId, "bookable_resource", id,
                existing.IsActive && !after.IsActive ? AuditActions.Deactivated : AuditActions.Updated, existing, after), cancellationToken);
            await PublishAsync(tenantId, existing.VenueId, id, cancellationToken);
            return after;
        }, cancellationToken);
    }

    public async Task<DeleteResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var existing = await GetAccessibleAsync(id, cancellationToken);
        var tenantId = tenantContext.TenantId;

        return await unitOfWork.ExecuteAsync(async () =>
        {
            var result = await resources.DeleteOrDeactivateAsync(tenantId, id, cancellationToken);
            await audit.WriteAsync(new AuditEntry(tenantId, "bookable_resource", id,
                result.Deleted ? AuditActions.Deleted : AuditActions.Deactivated, existing, null), cancellationToken);
            await PublishAsync(tenantId, existing.VenueId, id, cancellationToken);
            return result;
        }, cancellationToken);
    }

    private Task PublishAsync(Guid tenantId, Guid venueId, Guid resourceId, CancellationToken cancellationToken) =>
        outbox.WriteRealtimeAsync(tenantId, RealtimeEvents.ResourceUpdated, venueId, resourceId, null, null, clock.UtcNow, cancellationToken);

    private async Task<ResourceDto> GetAccessibleAsync(Guid id, CancellationToken cancellationToken)
    {
        var resource = await resources.GetAsync(tenantContext.TenantId, id, cancellationToken)
            ?? throw new NotFoundAppException(ErrorCodes.ResourceNotFound, NotFoundMessage);
        try
        {
            await guard.EnsureAccessAsync(resource.VenueId, cancellationToken);
        }
        catch (NotFoundAppException)
        {
            throw new NotFoundAppException(ErrorCodes.ResourceNotFound, NotFoundMessage);
        }

        return resource;
    }

    private async Task EnsureReferencesAsync(Guid tenantId, Guid venueId, Guid? sectionId, Guid resourceTypeId, CancellationToken cancellationToken)
    {
        if (sectionId is { } section && await venues.GetSectionAsync(tenantId, venueId, section, cancellationToken) is null)
        {
            throw new ValidationAppException("sectionId", "The section does not belong to this venue.");
        }

        if (await resources.GetTypeAsync(tenantId, resourceTypeId, cancellationToken) is null)
        {
            throw new ValidationAppException("resourceTypeId", "The resource type was not found.");
        }
    }
}
