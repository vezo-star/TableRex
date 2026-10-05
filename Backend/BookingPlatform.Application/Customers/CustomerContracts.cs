using BookingPlatform.Application.Common.Pagination;

namespace BookingPlatform.Application.Customers;

/// <summary>A tenant-wide guest record. Anonymised customers keep their id and bookings but no personal data.</summary>
public sealed record CustomerDto(
    Guid Id,
    string FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    string? Notes,
    bool MarketingConsent,
    bool IsAnonymized,
    DateTime? AnonymizedAt,
    int BookingCount,
    DateTime? LastBookingAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CustomerListQuery(string? Q, PageRequest Page);

public sealed record CreateCustomerRequest(
    string FirstName,
    string? LastName = null,
    string? Email = null,
    string? Phone = null,
    string? Notes = null,
    bool MarketingConsent = false);

public sealed record UpdateCustomerRequest(
    string FirstName,
    string? LastName = null,
    string? Email = null,
    string? Phone = null,
    string? Notes = null,
    bool MarketingConsent = false);

/// <summary>Normalised customer values ready to persist.</summary>
public sealed record CustomerValues(
    string FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    string? PhoneNormalized,
    string? Notes,
    bool MarketingConsent);

public interface ICustomerRepository
{
    Task<PagedResult<CustomerDto>> ListAsync(Guid tenantId, CustomerListQuery query, CancellationToken cancellationToken);

    Task<CustomerDto?> GetAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken);

    /// <summary>Another active (not anonymised) customer with the same email, if any.</summary>
    Task<Guid?> FindByEmailAsync(Guid tenantId, string email, Guid? excludeId, CancellationToken cancellationToken);

    Task<Guid> InsertAsync(Guid tenantId, CustomerValues values, CancellationToken cancellationToken);

    Task UpdateAsync(Guid tenantId, Guid customerId, CustomerValues values, DateTime now, CancellationToken cancellationToken);

    /// <summary>Removes personal data from the customer and the free-text fields of their bookings.</summary>
    Task<bool> AnonymizeAsync(Guid tenantId, Guid customerId, DateTime now, CancellationToken cancellationToken);
}
