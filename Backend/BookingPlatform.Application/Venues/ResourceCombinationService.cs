using BookingPlatform.Application.Common.Auditing;
using BookingPlatform.Application.Common.Errors;
using BookingPlatform.Application.Common.Persistence;
using BookingPlatform.Application.Common.Security;
using BookingPlatform.Application.Common.Validation;
using FluentValidation;

namespace BookingPlatform.Application.Venues;

public sealed record CombinationMemberDto(Guid ResourceId, bool IsRequired = true);

public sealed record ResourceCombinationDto(
    Guid Id,
    Guid VenueId,
    string Name,
    int MinCapacity,
    int Capacity,
    bool IsActive,
    bool IsBookableOnline,
    int SortOrder,
    IReadOnlyList<CombinationMemberDto> Members,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CreateResourceCombinationRequest(
    Guid VenueId,
    string Name,
    int MinCapacity,
    int Capacity,
    IReadOnlyList<CombinationMemberDto> Members,
    bool IsBookableOnline = true,
    int SortOrder = 0);

public sealed record UpdateResourceCombinationRequest(
    string Name,
    int MinCapacity,
    int Capacity,
    bool IsActive,
    bool IsBookableOnline,
    int SortOrder,
    IReadOnlyList<CombinationMemberDto> Members);

public interface IResourceCombinationRepository
{
    Task<IReadOnlyList<ResourceCombinationDto>> ListAsync(Guid tenantId, Guid venueId, CancellationToken cancellationToken);

    Task<ResourceCombinationDto?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);

    Task<bool> NameExistsAsync(Guid tenantId, Guid venueId, string name, Guid? exceptId, CancellationToken cancellationToken);

    Task<Guid> CreateAsync(Guid tenantId, CreateResourceCombinationRequest request, CancellationToken cancellationToken);

    Task UpdateAsync(Guid tenantId, Guid venueId, Guid id, UpdateResourceCombinationRequest request, CancellationToken cancellationToken);

    /// <summary>Deletes the combination, or deactivates it when bookings reference it.</summary>
    Task<DeleteResult> DeleteOrDeactivateAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
}

public sealed class ResourceCombinationService(
    IResourceCombinationRepository combinations,
    IResourceRepository resources,
    IVenueAccessGuard guard,
    IAuditWriter audit,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    IValidator<CreateResourceCombinationRequest> createValidator,
    IValidator<UpdateResourceCombinationRequest> updateValidator)
{
    private const string EntityType = "resource_combination";
    private const string NotFoundMessage = "The combination was not found.";
    private const string NameTakenMessage = "A combination with that name already exists in this venue.";

    public async Task<IReadOnlyList<ResourceCombinationDto>> ListAsync(Guid venueId, CancellationToken cancellationToken)
    {
        await guard.EnsureAccessAsync(venueId, cancellationToken);
        return await combinations.ListAsync(tenantContext.TenantId, venueId, cancellationToken);
    }

    public Task<ResourceCombinationDto> GetAsync(Guid id, CancellationToken cancellationToken) => GetAccessibleAsync(id, cancellationToken);

    public async Task<ResourceCombinationDto> CreateAsync(CreateResourceCombinationRequest request, CancellationToken cancellationToken)
    {
        await createValidator.EnsureValidAsync(request, cancellationToken);
        await guard.EnsureAccessAsync(request.VenueId, cancellationToken);
        var tenantId = tenantContext.TenantId;
        var normalised = request with { Name = request.Name.Trim() };

        return await unitOfWork.ExecuteAsync(async () =>
        {
            await EnsureMembersAsync(tenantId, request.VenueId, request.Members, cancellationToken);
            if (await combinations.NameExistsAsync(tenantId, request.VenueId, normalised.Name, null, cancellationToken))
            {
                throw new ConflictAppException(ErrorCodes.Conflict, NameTakenMessage);
            }

            var id = await combinations.CreateAsync(tenantId, normalised, cancellationToken);
            var created = (await combinations.GetAsync(tenantId, id, cancellationToken))!;
            await audit.WriteAsync(new AuditEntry(tenantId, EntityType, id, AuditActions.Created, NewValues: created), cancellationToken);
            return created;
        }, cancellationToken);
    }

    public async Task<ResourceCombinationDto> UpdateAsync(Guid id, UpdateResourceCombinationRequest request, CancellationToken cancellationToken)
    {
        await updateValidator.EnsureValidAsync(request, cancellationToken);
        var existing = await GetAccessibleAsync(id, cancellationToken);
        var tenantId = tenantContext.TenantId;
        var normalised = request with { Name = request.Name.Trim() };

        return await unitOfWork.ExecuteAsync(async () =>
        {
            await EnsureMembersAsync(tenantId, existing.VenueId, request.Members, cancellationToken);
            if (await combinations.NameExistsAsync(tenantId, existing.VenueId, normalised.Name, id, cancellationToken))
            {
                throw new ConflictAppException(ErrorCodes.Conflict, NameTakenMessage);
            }

            await combinations.UpdateAsync(tenantId, existing.VenueId, id, normalised, cancellationToken);
            var after = (await combinations.GetAsync(tenantId, id, cancellationToken))!;
            await audit.WriteAsync(new AuditEntry(tenantId, EntityType, id,
                existing.IsActive && !after.IsActive ? AuditActions.Deactivated : AuditActions.Updated, existing, after), cancellationToken);
            return after;
        }, cancellationToken);
    }

    public async Task<DeleteResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var existing = await GetAccessibleAsync(id, cancellationToken);
        var tenantId = tenantContext.TenantId;

        return await unitOfWork.ExecuteAsync(async () =>
        {
            var result = await combinations.DeleteOrDeactivateAsync(tenantId, id, cancellationToken);
            await audit.WriteAsync(new AuditEntry(tenantId, EntityType, id,
                result.Deleted ? AuditActions.Deleted : AuditActions.Deactivated, existing, null), cancellationToken);
            return result;
        }, cancellationToken);
    }

    private async Task<ResourceCombinationDto> GetAccessibleAsync(Guid id, CancellationToken cancellationToken)
    {
        var combination = await combinations.GetAsync(tenantContext.TenantId, id, cancellationToken)
            ?? throw new NotFoundAppException(ErrorCodes.CombinationNotFound, NotFoundMessage);
        try
        {
            await guard.EnsureAccessAsync(combination.VenueId, cancellationToken);
        }
        catch (NotFoundAppException)
        {
            throw new NotFoundAppException(ErrorCodes.CombinationNotFound, NotFoundMessage);
        }

        return combination;
    }

    private async Task EnsureMembersAsync(Guid tenantId, Guid venueId, IReadOnlyList<CombinationMemberDto> members, CancellationToken cancellationToken)
    {
        var venueResources = (await resources.ListByVenueAsync(tenantId, venueId, cancellationToken)).ToDictionary(r => r.Id);
        if (members.Any(m => !venueResources.ContainsKey(m.ResourceId)))
        {
            throw new ValidationAppException("members", "Every member must be a resource in this venue.");
        }
    }
}

internal static class CombinationRules
{
    public const int MaxMembers = 20;

    public static IRuleBuilderOptions<T, IReadOnlyList<CombinationMemberDto>> ValidMembers<T>(
        this IRuleBuilder<T, IReadOnlyList<CombinationMemberDto>> rule) =>
        rule.NotNull()
            .Must(m => m is { Count: >= 2 and <= MaxMembers }).WithMessage($"A combination needs between 2 and {MaxMembers} resources.")
            .Must(m => m is null || m.All(x => x.ResourceId != Guid.Empty)).WithMessage("Every member needs a resource id.")
            .Must(m => m is null || m.Select(x => x.ResourceId).Distinct().Count() == m.Count).WithMessage("Each resource may appear only once.")
            .Must(m => m is null || m.Any(x => x.IsRequired)).WithMessage("At least one member must be required.");
}

public sealed class CreateResourceCombinationRequestValidator : AbstractValidator<CreateResourceCombinationRequest>
{
    public CreateResourceCombinationRequestValidator()
    {
        RuleFor(r => r.VenueId).NotEmpty();
        RuleFor(r => r.Name).ValidName();
        RuleFor(r => r.MinCapacity).InclusiveBetween(1, 1000);
        RuleFor(r => r.Capacity).InclusiveBetween(1, 1000).GreaterThanOrEqualTo(r => r.MinCapacity)
            .WithMessage("Capacity must be at least the minimum capacity.");
        RuleFor(r => r.SortOrder).InclusiveBetween(0, 10000);
        RuleFor(r => r.Members).ValidMembers();
    }
}

public sealed class UpdateResourceCombinationRequestValidator : AbstractValidator<UpdateResourceCombinationRequest>
{
    public UpdateResourceCombinationRequestValidator()
    {
        RuleFor(r => r.Name).ValidName();
        RuleFor(r => r.MinCapacity).InclusiveBetween(1, 1000);
        RuleFor(r => r.Capacity).InclusiveBetween(1, 1000).GreaterThanOrEqualTo(r => r.MinCapacity)
            .WithMessage("Capacity must be at least the minimum capacity.");
        RuleFor(r => r.SortOrder).InclusiveBetween(0, 10000);
        RuleFor(r => r.Members).ValidMembers();
    }
}
