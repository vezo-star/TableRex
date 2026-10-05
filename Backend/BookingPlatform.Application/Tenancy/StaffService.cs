using BookingPlatform.Application.Common.Auditing;
using BookingPlatform.Application.Common.Errors;
using BookingPlatform.Application.Common.Pagination;
using BookingPlatform.Application.Common.Persistence;
using BookingPlatform.Application.Common.Security;
using BookingPlatform.Application.Common.Time;
using BookingPlatform.Application.Common.Validation;
using BookingPlatform.Application.Identity;
using BookingPlatform.Application.Subscriptions;
using BookingPlatform.Application.Venues;
using BookingPlatform.Domain.Identity;
using FluentValidation;

namespace BookingPlatform.Application.Tenancy;

public sealed class StaffService(
    IStaffRepository staff,
    ITenantRepository tenants,
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    IRoleRepository roles,
    IPasswordHasher passwordHasher,
    ITenantAccessCache accessCache,
    IAuditWriter audit,
    IUnitOfWork unitOfWork,
    IClock clock,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IValidator<CreateStaffRequest> createValidator,
    IValidator<UpdateStaffRolesRequest> rolesValidator,
    IValidator<StaffQuery> queryValidator,
    IVenueAccessRepository venueAccess,
    IValidator<UpdateStaffVenuesRequest> venuesValidator,
    PlanLimits planLimits)
{
    private const string NotFoundMessage = "The staff member was not found.";

    public async Task<PagedResult<StaffMemberDto>> ListAsync(StaffQuery query, CancellationToken cancellationToken)
    {
        await queryValidator.EnsureValidAsync(query, cancellationToken);
        return await staff.ListAsync(tenantContext.TenantId, query, cancellationToken);
    }

    public async Task<StaffMemberDto> GetAsync(Guid tenantUserId, CancellationToken cancellationToken) =>
        await staff.GetAsync(tenantContext.TenantId, tenantUserId, cancellationToken)
        ?? throw new NotFoundAppException(ErrorCodes.StaffNotFound, NotFoundMessage);

    public Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken cancellationToken) =>
        roles.ListSystemRolesAsync(cancellationToken);

    public async Task<CreateStaffResult> CreateAsync(CreateStaffRequest request, CancellationToken cancellationToken)
    {
        await createValidator.EnsureValidAsync(request, cancellationToken);
        var tenantId = tenantContext.TenantId;
        var requestedRoles = request.Roles.Distinct(StringComparer.Ordinal).ToList();
        EnsureCanAssign(requestedRoles, []);

        var email = request.Email.Trim().ToLowerInvariant();
        var newHash = string.IsNullOrEmpty(request.Password) ? null : passwordHasher.Hash(request.Password);

        return await unitOfWork.ExecuteAsync(async () =>
        {
            var existing = await users.GetByEmailAsync(email, cancellationToken);
            var accountCreated = false;
            Guid userId;
            if (existing is not null)
            {
                userId = existing.Id;
                if (await staff.FindMembershipAsync(tenantId, userId, cancellationToken) is not null)
                {
                    throw new ConflictAppException(ErrorCodes.EmailTaken, "This person is already a staff member of this tenant.");
                }
            }
            else
            {
                if (newHash is null)
                {
                    throw new ValidationAppException("password", "An initial password is required for a new account.");
                }

                userId = await users.CreateAsync(email, newHash, request.DisplayName.Trim(), false, cancellationToken);
                accountCreated = true;
            }

            await planLimits.EnsureCanAddStaffAsync(tenantId, cancellationToken);
            var tenantUserId = await staff.CreateMembershipAsync(tenantId, userId, cancellationToken);
            await staff.ReplaceRolesAsync(tenantId, tenantUserId, requestedRoles, cancellationToken);
            await audit.WriteAsync(new AuditEntry(tenantId, "tenant_user", tenantUserId, AuditActions.Created,
                NewValues: new { userId, roles = requestedRoles, accountCreated }), cancellationToken);

            var created = (await staff.GetAsync(tenantId, tenantUserId, cancellationToken))!;
            return new CreateStaffResult(created, accountCreated);
        }, cancellationToken);
    }

    public async Task<StaffMemberDto> UpdateRolesAsync(Guid tenantUserId, UpdateStaffRolesRequest request, CancellationToken cancellationToken)
    {
        await rolesValidator.EnsureValidAsync(request, cancellationToken);
        var tenantId = tenantContext.TenantId;
        var requestedRoles = request.Roles.Distinct(StringComparer.Ordinal).ToList();

        var result = await unitOfWork.ExecuteAsync(async () =>
        {
            await tenants.LockAsync(tenantId, cancellationToken);
            var member = await staff.GetAsync(tenantId, tenantUserId, cancellationToken)
                ?? throw new NotFoundAppException(ErrorCodes.StaffNotFound, NotFoundMessage);

            EnsureCanAssign(requestedRoles, member.Roles);

            var losesOwner = member.Roles.Contains(Roles.TenantOwner) && !requestedRoles.Contains(Roles.TenantOwner);
            if (losesOwner && member.Status == MembershipStatus.Active && await staff.CountActiveOwnersAsync(tenantId, cancellationToken) <= 1)
            {
                throw new BusinessRuleAppException(ErrorCodes.LastOwner, "The tenant must keep at least one active owner.");
            }

            await staff.ReplaceRolesAsync(tenantId, tenantUserId, requestedRoles, cancellationToken);
            await audit.WriteAsync(new AuditEntry(tenantId, "tenant_user", tenantUserId, AuditActions.RolesChanged,
                new { roles = member.Roles }, new { roles = requestedRoles.Order(StringComparer.Ordinal).ToList() }), cancellationToken);

            return (await staff.GetAsync(tenantId, tenantUserId, cancellationToken))!;
        }, cancellationToken);

        accessCache.Invalidate(result.UserId, tenantId);
        return result;
    }

    /// <summary>Replaces the venues assigned to a member (D-018). Owners and admins access every venue regardless.</summary>
    public async Task<StaffMemberDto> UpdateVenuesAsync(Guid tenantUserId, UpdateStaffVenuesRequest request, CancellationToken cancellationToken)
    {
        await venuesValidator.EnsureValidAsync(request, cancellationToken);
        var tenantId = tenantContext.TenantId;
        var requested = request.VenueIds.Distinct().ToList();

        return await unitOfWork.ExecuteAsync(async () =>
        {
            var member = await staff.GetAsync(tenantId, tenantUserId, cancellationToken)
                ?? throw new NotFoundAppException(ErrorCodes.StaffNotFound, NotFoundMessage);

            var tenantVenues = (await venueAccess.ListAllVenueIdsAsync(tenantId, cancellationToken)).ToHashSet();
            if (requested.Any(id => !tenantVenues.Contains(id)))
            {
                throw new ValidationAppException("venueIds", "One or more venues were not found.");
            }

            await venueAccess.ReplaceAssignmentsAsync(tenantId, tenantUserId, requested, cancellationToken);
            await audit.WriteAsync(new AuditEntry(tenantId, "tenant_user", tenantUserId, AuditActions.VenuesChanged,
                new { venueIds = member.VenueIds }, new { venueIds = requested.Order().ToList() }), cancellationToken);

            return (await staff.GetAsync(tenantId, tenantUserId, cancellationToken))!;
        }, cancellationToken);
    }

    public Task<StaffMemberDto> DeactivateAsync(Guid tenantUserId, CancellationToken cancellationToken) =>
        SetStatusAsync(tenantUserId, MembershipStatus.Disabled, cancellationToken);

    public Task<StaffMemberDto> ActivateAsync(Guid tenantUserId, CancellationToken cancellationToken) =>
        SetStatusAsync(tenantUserId, MembershipStatus.Active, cancellationToken);

    private async Task<StaffMemberDto> SetStatusAsync(Guid tenantUserId, string status, CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId;

        var result = await unitOfWork.ExecuteAsync(async () =>
        {
            await tenants.LockAsync(tenantId, cancellationToken);
            var member = await staff.GetAsync(tenantId, tenantUserId, cancellationToken)
                ?? throw new NotFoundAppException(ErrorCodes.StaffNotFound, NotFoundMessage);

            if (member.Status == status)
            {
                return member;
            }

            if (status == MembershipStatus.Disabled)
            {
                if (member.UserId == currentUser.UserId)
                {
                    throw new BusinessRuleAppException(ErrorCodes.BusinessRuleViolation, "You cannot deactivate your own membership.");
                }

                if (member.Roles.Contains(Roles.TenantOwner))
                {
                    EnsureCanAssign([], member.Roles);
                    if (await staff.CountActiveOwnersAsync(tenantId, cancellationToken) <= 1)
                    {
                        throw new BusinessRuleAppException(ErrorCodes.LastOwner, "The tenant must keep at least one active owner.");
                    }
                }

                await refreshTokens.RevokeAllForUserInTenantAsync(member.UserId, tenantId, RevocationReasons.MembershipRevoked, clock.UtcNow, cancellationToken);
            }
            else if (status == MembershipStatus.Active)
            {
                await planLimits.EnsureCanAddStaffAsync(tenantId, cancellationToken);
            }

            await staff.SetStatusAsync(tenantId, tenantUserId, status, cancellationToken);
            await audit.WriteAsync(new AuditEntry(tenantId, "tenant_user", tenantUserId,
                status == MembershipStatus.Disabled ? AuditActions.Deactivated : AuditActions.StatusChanged,
                new { status = member.Status }, new { status }), cancellationToken);

            return (await staff.GetAsync(tenantId, tenantUserId, cancellationToken))!;
        }, cancellationToken);

        accessCache.Invalidate(result.UserId, tenantId);
        return result;
    }

    /// <summary>Only owners may grant or revoke TENANT_OWNER (prevents admins escalating to owner or demoting owners).</summary>
    private void EnsureCanAssign(IReadOnlyCollection<string> requested, IReadOnlyCollection<string> current)
    {
        var touchesOwner = requested.Contains(Roles.TenantOwner) != current.Contains(Roles.TenantOwner);
        if (touchesOwner && !tenantContext.Roles.Contains(Roles.TenantOwner))
        {
            throw new ForbiddenAppException("Only an owner can grant or remove the owner role.");
        }
    }
}
