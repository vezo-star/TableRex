using BookingPlatform.Application.Common.Auditing;
using BookingPlatform.Application.Common.Errors;
using BookingPlatform.Application.Common.Pagination;
using BookingPlatform.Application.Common.Persistence;
using BookingPlatform.Application.Common.Security;
using BookingPlatform.Application.Common.Time;
using BookingPlatform.Application.Common.Validation;
using BookingPlatform.Application.Identity;
using BookingPlatform.Application.Subscriptions;
using FluentValidation;

namespace BookingPlatform.Application.Tenancy;

public sealed class TenantService(
    ITenantRepository tenants,
    IStaffRepository staff,
    IUserRepository users,
    IPasswordHasher passwordHasher,
    IAuditWriter audit,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    ISubscriptionRepository subscriptions,
    IClock clock,
    IValidator<UpdateTenantRequest> updateValidator,
    IValidator<CreateTenantRequest> createValidator)
{
    public async Task<TenantDto> GetCurrentAsync(CancellationToken cancellationToken) =>
        await tenants.GetAsync(tenantContext.TenantId, cancellationToken)
        ?? throw new NotFoundAppException(ErrorCodes.TenantNotFound, "The tenant was not found.");

    public async Task<TenantDto> UpdateCurrentAsync(UpdateTenantRequest request, CancellationToken cancellationToken)
    {
        await updateValidator.EnsureValidAsync(request, cancellationToken);
        var tenantId = tenantContext.TenantId;

        return await unitOfWork.ExecuteAsync(async () =>
        {
            var before = await tenants.GetAsync(tenantId, cancellationToken)
                ?? throw new NotFoundAppException(ErrorCodes.TenantNotFound, "The tenant was not found.");
            await tenants.UpdateAsync(tenantId, request.Name.Trim(), request.DefaultTimeZone, cancellationToken);
            var after = (await tenants.GetAsync(tenantId, cancellationToken))!;
            await audit.WriteAsync(new AuditEntry(tenantId, "tenant", tenantId, AuditActions.Updated,
                new { before.Name, before.DefaultTimeZone }, new { after.Name, after.DefaultTimeZone }), cancellationToken);
            return after;
        }, cancellationToken);
    }

    public Task<PagedResult<TenantDto>> ListAllAsync(PageRequest page, CancellationToken cancellationToken) =>
        tenants.ListAsync(page, cancellationToken);

    /// <summary>Platform admin: creates a tenant and its first owner (existing account or a new one).</summary>
    public async Task<TenantDto> CreateAsync(CreateTenantRequest request, CancellationToken cancellationToken)
    {
        await createValidator.EnsureValidAsync(request, cancellationToken);
        var email = request.Owner.Email.Trim().ToLowerInvariant();

        return await unitOfWork.ExecuteAsync(async () =>
        {
            if (await tenants.SlugExistsAsync(request.Slug, cancellationToken))
            {
                throw new ConflictAppException(ErrorCodes.SlugTaken, "That slug is already in use.");
            }

            if (request.PlanCode is not null && !await subscriptions.PlanExistsAsync(request.PlanCode, cancellationToken))
            {
                throw new ValidationAppException("planCode", "Unknown plan.");
            }

            var tenantId = await tenants.CreateAsync(request.Name.Trim(), request.Slug, request.DefaultTimeZone, cancellationToken);
            if (request.PlanCode is not null)
            {
                await subscriptions.UpsertAsync(tenantId, new UpdateSubscriptionRequest(request.PlanCode), clock.UtcNow, cancellationToken);
            }

            var existing = await users.GetByEmailAsync(email, cancellationToken);
            Guid userId;
            if (existing is not null)
            {
                userId = existing.Id;
            }
            else
            {
                if (string.IsNullOrEmpty(request.Owner.Password))
                {
                    throw new ValidationAppException("owner.password", "A password is required for a new account.");
                }

                userId = await users.CreateAsync(email, passwordHasher.Hash(request.Owner.Password), request.Owner.DisplayName.Trim(), false, cancellationToken);
            }

            var tenantUserId = await staff.CreateMembershipAsync(tenantId, userId, cancellationToken);
            await staff.ReplaceRolesAsync(tenantId, tenantUserId, [Roles.TenantOwner], cancellationToken);

            await audit.WriteAsync(new AuditEntry(tenantId, "tenant", tenantId, AuditActions.Created,
                NewValues: new { request.Name, request.Slug, request.DefaultTimeZone, request.PlanCode, ownerUserId = userId }), cancellationToken);

            return (await tenants.GetAsync(tenantId, cancellationToken))!;
        }, cancellationToken);
    }
}
