using BookingPlatform.Api.Security;
using BookingPlatform.Application.Auditing;
using BookingPlatform.Application.Bookings;
using BookingPlatform.Application.Common.Pagination;
using BookingPlatform.Application.Common.Security;
using BookingPlatform.Application.Customers;
using Microsoft.AspNetCore.Mvc;

namespace BookingPlatform.Api.Controllers;

[ApiController]
[Route("api/v1/customers")]
[Produces("application/json")]
public sealed class CustomersController(CustomerService customers, BookingService bookings) : ControllerBase
{
    /// <summary>Tenant-wide customer search; <c>q</c> matches name, email and phone digits.</summary>
    [HttpGet]
    [HasPermission(Permissions.CustomerView)]
    public Task<PagedResult<CustomerDto>> List(
        [FromQuery] string? q,
        [FromQuery] int page = PageRequest.DefaultPage,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        customers.ListAsync(new CustomerListQuery(q, new PageRequest(page, pageSize)), cancellationToken);

    [HttpPost]
    [HasPermission(Permissions.CustomerManage)]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CustomerDto>> Create(CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        var customer = await customers.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = customer.Id }, customer);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.CustomerView)]
    public Task<CustomerDto> Get(Guid id, CancellationToken cancellationToken) => customers.GetAsync(id, cancellationToken);

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.CustomerManage)]
    public Task<CustomerDto> Update(Guid id, UpdateCustomerRequest request, CancellationToken cancellationToken) =>
        customers.UpdateAsync(id, request, cancellationToken);

    /// <summary>The customer's bookings at venues the caller can access, newest first.</summary>
    [HttpGet("{id:guid}/bookings")]
    [HasPermission(Permissions.CustomerView)]
    [HasPermission(Permissions.BookingView)]
    public async Task<PagedResult<BookingDto>> Bookings(
        Guid id,
        [FromQuery] int page = PageRequest.DefaultPage,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        await customers.GetAsync(id, cancellationToken);
        return await bookings.ListAsync(
            new BookingListQuery(null, null, null, null, null, null, id, null, null, null, new PageRequest(page, pageSize)) { NewestFirst = true },
            cancellationToken);
    }

    /// <summary>Removes the customer's personal data (POPIA erasure). Bookings remain for reporting.</summary>
    [HttpPost("{id:guid}/anonymize")]
    [HasPermission(Permissions.CustomerManage)]
    public Task<CustomerDto> Anonymize(Guid id, CancellationToken cancellationToken) => customers.AnonymizeAsync(id, cancellationToken);
}

[ApiController]
[Route("api/v1/audit-logs")]
[Produces("application/json")]
public sealed class AuditLogsController(AuditLogService auditLogs) : ControllerBase
{
    /// <summary>The tenant's audit trail, newest first. Pass <c>nextCursor</c> as <c>before</c> for the next page.</summary>
    [HttpGet]
    [HasPermission(Permissions.SettingsManage)]
    public Task<AuditLogPage> List(
        [FromQuery] string? entityType,
        [FromQuery] Guid? entityId,
        [FromQuery] Guid? userId,
        [FromQuery] string? action,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? before,
        [FromQuery] int limit = AuditLogQuery.DefaultLimit,
        CancellationToken cancellationToken = default) =>
        auditLogs.ListAsync(new AuditLogQuery(entityType, entityId, userId, action, from, to, before, limit), cancellationToken);
}
