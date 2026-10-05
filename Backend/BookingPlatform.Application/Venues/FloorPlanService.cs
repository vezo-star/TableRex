using BookingPlatform.Application.Common.Auditing;
using BookingPlatform.Application.Common.Errors;
using BookingPlatform.Application.Common.Persistence;
using BookingPlatform.Application.Common.Security;
using BookingPlatform.Application.Common.Validation;
using FluentValidation;

namespace BookingPlatform.Application.Venues;

public sealed class FloorPlanService(
    IFloorPlanRepository floorPlans,
    IResourceRepository resources,
    IVenueAccessGuard guard,
    IAuditWriter audit,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    IValidator<CreateFloorPlanRequest> createValidator,
    IValidator<UpdateFloorPlanRequest> updateValidator,
    IValidator<ReplaceFloorPlanPositionsRequest> positionsValidator)
{
    private const string NotFoundMessage = "The floor plan was not found.";

    public async Task<IReadOnlyList<FloorPlanDto>> ListAsync(Guid venueId, CancellationToken cancellationToken)
    {
        await guard.EnsureAccessAsync(venueId, cancellationToken);
        return await floorPlans.ListAsync(tenantContext.TenantId, venueId, cancellationToken);
    }

    public Task<FloorPlanDto> GetAsync(Guid id, CancellationToken cancellationToken) => GetAccessibleAsync(id, cancellationToken);

    public async Task<FloorPlanDto> CreateAsync(CreateFloorPlanRequest request, CancellationToken cancellationToken)
    {
        await createValidator.EnsureValidAsync(request, cancellationToken);
        await guard.EnsureAccessAsync(request.VenueId, cancellationToken);
        var tenantId = tenantContext.TenantId;

        return await unitOfWork.ExecuteAsync(async () =>
        {
            var id = await floorPlans.CreateAsync(tenantId, request with { Name = request.Name.Trim() }, cancellationToken);
            var created = (await floorPlans.GetAsync(tenantId, id, cancellationToken))!;
            await audit.WriteAsync(new AuditEntry(tenantId, "floor_plan", id, AuditActions.Created,
                NewValues: new { created.VenueId, created.Name, created.Width, created.Height }), cancellationToken);
            return created;
        }, cancellationToken);
    }

    public async Task<FloorPlanDto> UpdateAsync(Guid id, UpdateFloorPlanRequest request, CancellationToken cancellationToken)
    {
        await updateValidator.EnsureValidAsync(request, cancellationToken);
        var before = await GetAccessibleAsync(id, cancellationToken);
        var tenantId = tenantContext.TenantId;

        return await unitOfWork.ExecuteAsync(async () =>
        {
            await floorPlans.UpdateAsync(tenantId, id, request with { Name = request.Name.Trim() }, cancellationToken);
            var after = (await floorPlans.GetAsync(tenantId, id, cancellationToken))!;
            await audit.WriteAsync(new AuditEntry(tenantId, "floor_plan", id, AuditActions.Updated,
                new { before.Name, before.Width, before.Height, before.SortOrder, before.IsActive },
                new { after.Name, after.Width, after.Height, after.SortOrder, after.IsActive }), cancellationToken);
            return after;
        }, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var before = await GetAccessibleAsync(id, cancellationToken);
        var tenantId = tenantContext.TenantId;

        await unitOfWork.ExecuteAsync(async () =>
        {
            await floorPlans.DeleteAsync(tenantId, id, cancellationToken);
            await audit.WriteAsync(new AuditEntry(tenantId, "floor_plan", id, AuditActions.Deleted,
                new { before.VenueId, before.Name, positions = before.Positions.Count }, null), cancellationToken);
        }, cancellationToken);
    }

    /// <summary>Replaces every resource position on the plan. Resources must belong to the plan's venue.</summary>
    public async Task<FloorPlanDto> ReplacePositionsAsync(Guid id, ReplaceFloorPlanPositionsRequest request, CancellationToken cancellationToken)
    {
        await positionsValidator.EnsureValidAsync(request, cancellationToken);
        var plan = await GetAccessibleAsync(id, cancellationToken);
        var tenantId = tenantContext.TenantId;

        var outside = request.Positions.FirstOrDefault(p => p.X + p.Width > plan.Width || p.Y + p.Height > plan.Height);
        if (outside is not null)
        {
            throw new ValidationAppException("positions", "Every resource must fit inside the floor plan.");
        }

        return await unitOfWork.ExecuteAsync(async () =>
        {
            var venueResources = (await resources.ListByVenueAsync(tenantId, plan.VenueId, cancellationToken)).Select(r => r.Id).ToHashSet();
            if (request.Positions.Any(p => !venueResources.Contains(p.ResourceId)))
            {
                throw new ValidationAppException("positions", "Every resource must belong to the floor plan's venue.");
            }

            await floorPlans.ReplacePositionsAsync(tenantId, plan.VenueId, id, request.Positions, cancellationToken);
            var after = (await floorPlans.GetAsync(tenantId, id, cancellationToken))!;
            await audit.WriteAsync(new AuditEntry(tenantId, "floor_plan", id, AuditActions.Moved,
                new { positions = plan.Positions.Count }, new { positions = after.Positions.Count }), cancellationToken);
            return after;
        }, cancellationToken);
    }

    private async Task<FloorPlanDto> GetAccessibleAsync(Guid id, CancellationToken cancellationToken)
    {
        var plan = await floorPlans.GetAsync(tenantContext.TenantId, id, cancellationToken)
            ?? throw new NotFoundAppException(ErrorCodes.FloorPlanNotFound, NotFoundMessage);
        try
        {
            await guard.EnsureAccessAsync(plan.VenueId, cancellationToken);
        }
        catch (NotFoundAppException)
        {
            throw new NotFoundAppException(ErrorCodes.FloorPlanNotFound, NotFoundMessage);
        }

        return plan;
    }
}
