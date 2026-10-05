using BookingPlatform.Application.Common.Pagination;
using BookingPlatform.Application.Customers;
using BookingPlatform.Infrastructure.Persistence;

namespace BookingPlatform.Infrastructure.Customers;

public sealed class CustomerRepository(DbSession db) : ICustomerRepository
{
    private const string Select = """
        SELECT c.id, c.first_name, c.last_name, c.email::text AS email, c.phone, c.notes, c.marketing_consent,
               c.anonymized_at IS NOT NULL AS is_anonymized, c.anonymized_at,
               coalesce(s.booking_count, 0)::int AS booking_count, s.last_booking_at, c.created_at, c.updated_at
        FROM customers c
        LEFT JOIN LATERAL (
            SELECT count(*) AS booking_count, max(b.starts_at) AS last_booking_at
            FROM bookings b WHERE b.tenant_id = c.tenant_id AND b.customer_id = c.id
        ) s ON true
        """;

    public async Task<PagedResult<CustomerDto>> ListAsync(Guid tenantId, CustomerListQuery query, CancellationToken cancellationToken)
    {
        const string filter = """
            WHERE c.tenant_id = @tenantId
              AND (@q::text IS NULL
                   OR (c.first_name || ' ' || coalesce(c.last_name, '')) ILIKE @qLike
                   OR c.email::text ILIKE @qLike
                   OR (@qDigits::text IS NOT NULL AND c.phone_normalized LIKE @qDigits))
            """;

        var q = string.IsNullOrWhiteSpace(query.Q) ? null : query.Q.Trim();
        var digits = q is null ? null : new string(q.Where(char.IsAsciiDigit).ToArray());
        await using var grid = await db.QueryMultipleAsync(
            $"""
            SELECT count(*) FROM customers c {filter};
            {Select} {filter}
            ORDER BY c.anonymized_at IS NOT NULL, lower(c.first_name), lower(coalesce(c.last_name, '')), c.id
            LIMIT @PageSize OFFSET @Offset;
            """,
            new
            {
                tenantId,
                q,
                qLike = SqlText.Contains(q),
                qDigits = digits is { Length: >= 3 } ? $"%{digits}%" : null,
                query.Page.PageSize,
                query.Page.Offset,
            },
            cancellationToken);

        var total = await grid.ReadSingleAsync<long>();
        var items = (await grid.ReadAsync<CustomerDto>()).ToList();
        return new PagedResult<CustomerDto>(items, query.Page.Page, query.Page.PageSize, total);
    }

    public Task<CustomerDto?> GetAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken) =>
        db.QuerySingleOrDefaultAsync<CustomerDto>($"{Select} WHERE c.tenant_id = @tenantId AND c.id = @customerId",
            new { tenantId, customerId }, cancellationToken);

    public Task<Guid?> FindByEmailAsync(Guid tenantId, string email, Guid? excludeId, CancellationToken cancellationToken) =>
        db.QuerySingleOrDefaultAsync<Guid?>(
            """
            SELECT id FROM customers
            WHERE tenant_id = @tenantId AND email = @email::citext AND anonymized_at IS NULL AND (@excludeId::uuid IS NULL OR id <> @excludeId)
            ORDER BY created_at, id LIMIT 1
            """,
            new { tenantId, email, excludeId }, cancellationToken);

    public Task<Guid> InsertAsync(Guid tenantId, CustomerValues values, CancellationToken cancellationToken) =>
        db.QuerySingleAsync<Guid>(
            """
            INSERT INTO customers (tenant_id, first_name, last_name, email, phone, phone_normalized, notes, marketing_consent)
            VALUES (@tenantId, @FirstName, @LastName, @Email, @Phone, @PhoneNormalized, @Notes, @MarketingConsent) RETURNING id
            """,
            new { tenantId, values.FirstName, values.LastName, values.Email, values.Phone, values.PhoneNormalized, values.Notes, values.MarketingConsent },
            cancellationToken);

    public Task UpdateAsync(Guid tenantId, Guid customerId, CustomerValues values, DateTime now, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            """
            UPDATE customers
            SET first_name = @FirstName, last_name = @LastName, email = @Email, phone = @Phone, phone_normalized = @PhoneNormalized,
                notes = @Notes, marketing_consent = @MarketingConsent, updated_at = @now
            WHERE tenant_id = @tenantId AND id = @customerId AND anonymized_at IS NULL
            """,
            new
            {
                tenantId,
                customerId,
                values.FirstName,
                values.LastName,
                values.Email,
                values.Phone,
                values.PhoneNormalized,
                values.Notes,
                values.MarketingConsent,
                now,
            },
            cancellationToken);

    public async Task<bool> AnonymizeAsync(Guid tenantId, Guid customerId, DateTime now, CancellationToken cancellationToken)
    {
        var changed = await db.ExecuteAsync(
            """
            UPDATE customers
            SET first_name = 'Anonymised', last_name = NULL, email = NULL, phone = NULL, phone_normalized = NULL, notes = NULL,
                preferences = '{}'::jsonb, marketing_consent = false, anonymized_at = @now, updated_at = @now
            WHERE tenant_id = @tenantId AND id = @customerId AND anonymized_at IS NULL
            """,
            new { tenantId, customerId, now }, cancellationToken);
        if (changed == 0)
        {
            return false;
        }

        await db.ExecuteAsync(
            """
            UPDATE bookings SET special_requests = NULL, internal_notes = NULL, version = version + 1, updated_at = @now
            WHERE tenant_id = @tenantId AND customer_id = @customerId AND (special_requests IS NOT NULL OR internal_notes IS NOT NULL)
            """,
            new { tenantId, customerId, now }, cancellationToken);
        return true;
    }
}
