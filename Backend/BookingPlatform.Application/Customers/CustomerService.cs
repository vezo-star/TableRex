using BookingPlatform.Application.Bookings;
using BookingPlatform.Application.Common.Auditing;
using BookingPlatform.Application.Common.Errors;
using BookingPlatform.Application.Common.Pagination;
using BookingPlatform.Application.Common.Persistence;
using BookingPlatform.Application.Common.Security;
using BookingPlatform.Application.Common.Validation;
using FluentValidation;
using IClock = BookingPlatform.Application.Common.Time.IClock;

namespace BookingPlatform.Application.Customers;

/// <summary>
/// Tenant-wide customer records (D-054). Audit rows record which fields changed, never the personal data itself,
/// because audit logs are append-only and must survive an erasure request.
/// </summary>
public sealed class CustomerService(
    ICustomerRepository customers,
    IAuditWriter audit,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    IClock clock,
    IValidator<CreateCustomerRequest> createValidator,
    IValidator<UpdateCustomerRequest> updateValidator,
    IValidator<CustomerListQuery> listValidator)
{
    private const string EntityType = "customer";

    public async Task<PagedResult<CustomerDto>> ListAsync(CustomerListQuery query, CancellationToken cancellationToken)
    {
        await listValidator.EnsureValidAsync(query, cancellationToken);
        return await customers.ListAsync(tenantContext.TenantId, query, cancellationToken);
    }

    public async Task<CustomerDto> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await customers.GetAsync(tenantContext.TenantId, id, cancellationToken) ?? throw NotFound();

    public async Task<CustomerDto> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await createValidator.EnsureValidAsync(request, cancellationToken);
        var tenantId = tenantContext.TenantId;
        var values = Normalise(request.FirstName, request.LastName, request.Email, request.Phone, request.Notes, request.MarketingConsent);

        return await unitOfWork.ExecuteAsync(async () =>
        {
            await EnsureEmailFreeAsync(tenantId, values.Email, null, cancellationToken);
            var id = await customers.InsertAsync(tenantId, values, cancellationToken);
            await audit.WriteAsync(new AuditEntry(tenantId, EntityType, id, AuditActions.Created, NewValues: new { fields = PresentFields(values) }), cancellationToken);
            return (await customers.GetAsync(tenantId, id, cancellationToken))!;
        }, cancellationToken);
    }

    public async Task<CustomerDto> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await updateValidator.EnsureValidAsync(request, cancellationToken);
        var tenantId = tenantContext.TenantId;
        var values = Normalise(request.FirstName, request.LastName, request.Email, request.Phone, request.Notes, request.MarketingConsent);

        return await unitOfWork.ExecuteAsync(async () =>
        {
            var before = await customers.GetAsync(tenantId, id, cancellationToken) ?? throw NotFound();
            if (before.IsAnonymized)
            {
                throw new BusinessRuleAppException(ErrorCodes.CustomerAnonymized, "An anonymised customer cannot be edited.");
            }

            await EnsureEmailFreeAsync(tenantId, values.Email, id, cancellationToken);
            await customers.UpdateAsync(tenantId, id, values, clock.UtcNow, cancellationToken);
            var changed = ChangedFields(before, values);
            if (changed.Count > 0)
            {
                await audit.WriteAsync(new AuditEntry(tenantId, EntityType, id, AuditActions.Updated, NewValues: new { fields = changed }), cancellationToken);
            }

            return (await customers.GetAsync(tenantId, id, cancellationToken))!;
        }, cancellationToken);
    }

    /// <summary>POPIA erasure: personal data is removed, the record and its bookings remain for reporting. Repeating is a no-op.</summary>
    public async Task<CustomerDto> AnonymizeAsync(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId;
        return await unitOfWork.ExecuteAsync(async () =>
        {
            _ = await customers.GetAsync(tenantId, id, cancellationToken) ?? throw NotFound();
            if (await customers.AnonymizeAsync(tenantId, id, clock.UtcNow, cancellationToken))
            {
                await audit.WriteAsync(new AuditEntry(tenantId, EntityType, id, AuditActions.Anonymized), cancellationToken);
            }

            return (await customers.GetAsync(tenantId, id, cancellationToken))!;
        }, cancellationToken);
    }

    private async Task EnsureEmailFreeAsync(Guid tenantId, string? email, Guid? excludeId, CancellationToken cancellationToken)
    {
        if (email is not null && await customers.FindByEmailAsync(tenantId, email, excludeId, cancellationToken) is { } existing)
        {
            throw new ConflictAppException(ErrorCodes.EmailTaken, "Another customer already uses this email address.")
            {
                Extensions = new Dictionary<string, object?> { ["customerId"] = existing },
            };
        }
    }

    private static CustomerValues Normalise(string firstName, string? lastName, string? email, string? phone, string? notes, bool marketingConsent)
    {
        var cleanPhone = Clean(phone);
        return new CustomerValues(firstName.Trim(), Clean(lastName), Clean(email)?.ToLowerInvariant(), cleanPhone,
            BookingService.NormalizePhone(cleanPhone), Clean(notes), marketingConsent);
    }

    private static List<string> PresentFields(CustomerValues values) =>
        new (string Name, bool Present)[]
        {
            ("firstName", true),
            ("lastName", values.LastName is not null),
            ("email", values.Email is not null),
            ("phone", values.Phone is not null),
            ("notes", values.Notes is not null),
            ("marketingConsent", values.MarketingConsent),
        }.Where(f => f.Present).Select(f => f.Name).ToList();

    private static List<string> ChangedFields(CustomerDto before, CustomerValues after) =>
        new (string Name, bool Changed)[]
        {
            ("firstName", before.FirstName != after.FirstName),
            ("lastName", before.LastName != after.LastName),
            ("email", !string.Equals(before.Email, after.Email, StringComparison.OrdinalIgnoreCase)),
            ("phone", before.Phone != after.Phone),
            ("notes", before.Notes != after.Notes),
            ("marketingConsent", before.MarketingConsent != after.MarketingConsent),
        }.Where(f => f.Changed).Select(f => f.Name).ToList();

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static NotFoundAppException NotFound() => new(ErrorCodes.CustomerNotFound, "The customer was not found.");
}
