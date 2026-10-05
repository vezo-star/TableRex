using BookingPlatform.Api.Security;
using BookingPlatform.Application.Common.Pagination;
using BookingPlatform.Application.Common.Security;
using BookingPlatform.Application.Tenancy;
using BookingPlatform.Application.Venues;
using Microsoft.AspNetCore.Mvc;

namespace BookingPlatform.Api.Controllers;

[ApiController]
[Route("api/v1/tenants")]
[Produces("application/json")]
public sealed class TenantsController(TenantService tenants) : ControllerBase
{
    [HttpGet("current")]
    [RequireTenant]
    public Task<TenantDto> GetCurrent(CancellationToken cancellationToken) => tenants.GetCurrentAsync(cancellationToken);

    [HttpPut("current")]
    [HasPermission(Permissions.SettingsManage)]
    public Task<TenantDto> UpdateCurrent(UpdateTenantRequest request, CancellationToken cancellationToken) =>
        tenants.UpdateCurrentAsync(request, cancellationToken);
}

[ApiController]
[Route("api/v1/staff")]
[Produces("application/json")]
public sealed class StaffController(StaffService staff) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.StaffView)]
    public Task<PagedResult<StaffMemberDto>> List(
        [FromQuery] string? status, [FromQuery] string? q, [FromQuery] int page = PageRequest.DefaultPage,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize, CancellationToken cancellationToken = default) =>
        staff.ListAsync(new StaffQuery(status, q, new PageRequest(page, pageSize)), cancellationToken);

    [HttpGet("{tenantUserId:guid}")]
    [HasPermission(Permissions.StaffView)]
    public Task<StaffMemberDto> Get(Guid tenantUserId, CancellationToken cancellationToken) => staff.GetAsync(tenantUserId, cancellationToken);

    [HttpPost]
    [HasPermission(Permissions.StaffManage)]
    [ProducesResponseType<CreateStaffResult>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CreateStaffResult>> Create(CreateStaffRequest request, CancellationToken cancellationToken)
    {
        var result = await staff.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { tenantUserId = result.Staff.TenantUserId }, result);
    }

    [HttpPut("{tenantUserId:guid}/roles")]
    [HasPermission(Permissions.StaffManage)]
    public Task<StaffMemberDto> UpdateRoles(Guid tenantUserId, UpdateStaffRolesRequest request, CancellationToken cancellationToken) =>
        staff.UpdateRolesAsync(tenantUserId, request, cancellationToken);

    /// <summary>Replaces the venues a member may access (owners and admins always access every venue).</summary>
    [HttpPut("{tenantUserId:guid}/venues")]
    [HasPermission(Permissions.StaffManage)]
    public Task<StaffMemberDto> UpdateVenues(Guid tenantUserId, UpdateStaffVenuesRequest request, CancellationToken cancellationToken) =>
        staff.UpdateVenuesAsync(tenantUserId, request, cancellationToken);

    [HttpPost("{tenantUserId:guid}/deactivate")]
    [HasPermission(Permissions.StaffManage)]
    public Task<StaffMemberDto> Deactivate(Guid tenantUserId, CancellationToken cancellationToken) =>
        staff.DeactivateAsync(tenantUserId, cancellationToken);

    [HttpPost("{tenantUserId:guid}/activate")]
    [HasPermission(Permissions.StaffManage)]
    public Task<StaffMemberDto> Activate(Guid tenantUserId, CancellationToken cancellationToken) =>
        staff.ActivateAsync(tenantUserId, cancellationToken);
}

[ApiController]
[Route("api/v1/roles")]
[Produces("application/json")]
public sealed class RolesController(StaffService staff) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.StaffView)]
    public Task<IReadOnlyList<RoleDto>> List(CancellationToken cancellationToken) => staff.ListRolesAsync(cancellationToken);
}

/// <summary>Platform administration. Platform admins manage tenants only; they get no implicit tenant data access.</summary>
[ApiController]
[Route("api/v1/platform/tenants")]
[Produces("application/json")]
[RequirePlatformAdmin]
public sealed class PlatformTenantsController(TenantService tenants) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<TenantDto>>> List(
        [FromQuery] int page = PageRequest.DefaultPage, [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var request = new PageRequest(Math.Max(1, page), Math.Clamp(pageSize, 1, PageRequest.MaxPageSize));
        return await tenants.ListAllAsync(request, cancellationToken);
    }

    [HttpPost]
    [ProducesResponseType<TenantDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<TenantDto>> Create(CreateTenantRequest request, CancellationToken cancellationToken)
    {
        var tenant = await tenants.CreateAsync(request, cancellationToken);
        return Created($"/api/v1/platform/tenants/{tenant.Id}", tenant);
    }
}
