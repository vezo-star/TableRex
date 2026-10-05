using System.Text.Json;
using BookingPlatform.Application.Bookings;
using BookingPlatform.Application.Common.Pagination;
using BookingPlatform.Infrastructure.Persistence;

namespace BookingPlatform.Infrastructure.Bookings;

public sealed class BookingRepository(DbSession db) : IBookingRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string SelectBooking = """
        SELECT b.id, b.venue_id, v.time_zone, b.customer_id, b.reference, b.status, b.source, b.party_size, b.starts_at, b.ends_at,
               b.business_date, b.special_requests, b.internal_notes, b.expires_at, b.created_by_user_id, b.version, b.confirmed_at,
               b.checked_in_at, b.seated_at, b.completed_at, b.cancelled_at, b.no_show_at, b.cancellation_reason, b.created_at, b.updated_at,
               c.first_name AS customer_first_name, c.last_name AS customer_last_name, c.email::text AS customer_email, c.phone AS customer_phone
        FROM bookings b
        JOIN venues v ON v.tenant_id = b.tenant_id AND v.id = b.venue_id
        LEFT JOIN customers c ON c.tenant_id = b.tenant_id AND c.id = b.customer_id
        """;

    public Task<long> NextReferenceSequenceAsync(CancellationToken cancellationToken) =>
        db.QuerySingleAsync<long>("SELECT nextval('booking_reference_seq')", null, cancellationToken);

    public Task InsertAsync(Guid tenantId, NewBooking booking, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            """
            INSERT INTO bookings (id, tenant_id, venue_id, customer_id, reference, status, source, party_size, starts_at, ends_at, business_date,
                                  special_requests, internal_notes, created_by_user_id, public_token_hash, confirmed_at, seated_at, created_at, updated_at)
            VALUES (@Id, @tenantId, @VenueId, @CustomerId, @Reference, @Status, @Source, @PartySize, @StartsAt, @EndsAt, @BusinessDate,
                    @SpecialRequests, @InternalNotes, @CreatedByUserId, @PublicTokenHash,
                    CASE WHEN @Status = 'CONFIRMED' THEN @Now END, CASE WHEN @Status = 'SEATED' THEN @Now END, @Now, @Now)
            """,
            new
            {
                tenantId,
                booking.Id,
                booking.VenueId,
                booking.CustomerId,
                booking.Reference,
                booking.Status,
                booking.Source,
                booking.PartySize,
                booking.StartsAt,
                booking.EndsAt,
                booking.BusinessDate,
                booking.SpecialRequests,
                booking.InternalNotes,
                booking.CreatedByUserId,
                booking.PublicTokenHash,
                booking.Now,
            },
            cancellationToken);

    public Task<int> CountActiveUpcomingAsync(Guid tenantId, Guid venueId, Guid customerId, DateTime now, CancellationToken cancellationToken) =>
        db.QuerySingleAsync<int>(
            """
            SELECT count(*)::int FROM bookings
            WHERE tenant_id = @tenantId AND venue_id = @venueId AND customer_id = @customerId
              AND status IN ('PENDING', 'HELD', 'CONFIRMED') AND starts_at > @now
            """,
            new { tenantId, venueId, customerId, now },
            cancellationToken);

    public async Task<(Guid TenantId, Guid BookingId)?> FindByPublicTokenAsync(byte[] tokenHash, CancellationToken cancellationToken)
    {
        var row = await db.QuerySingleOrDefaultAsync<TokenRow>(
            "SELECT tenant_id, id FROM bookings WHERE public_token_hash = @tokenHash",
            new { tokenHash }, cancellationToken);
        return row is null ? null : (row.TenantId, row.Id);
    }

    public async Task<BookingRecord?> GetAsync(Guid tenantId, Guid bookingId, CancellationToken cancellationToken)
    {
        var row = await db.QuerySingleOrDefaultAsync<BookingRow>($"{SelectBooking} WHERE b.tenant_id = @tenantId AND b.id = @bookingId",
            new { tenantId, bookingId }, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var resources = await LoadResourcesAsync(tenantId, [row.Id], cancellationToken);
        return Map(row, resources);
    }

    public async Task<(Guid VenueId, string Status, int Version)?> LockAsync(Guid tenantId, Guid bookingId, CancellationToken cancellationToken)
    {
        var row = await db.QuerySingleOrDefaultAsync<LockRow>(
            "SELECT venue_id, status, version FROM bookings WHERE tenant_id = @tenantId AND id = @bookingId FOR UPDATE",
            new { tenantId, bookingId }, cancellationToken);
        return row is null ? null : (row.VenueId, row.Status, row.Version);
    }

    public async Task<PagedResult<BookingRecord>> ListAsync(Guid tenantId, BookingListQuery query, IReadOnlyCollection<Guid>? venueIds, CancellationToken cancellationToken)
    {
        const string filter = """
            WHERE b.tenant_id = @tenantId
              AND (@allVenues OR b.venue_id = ANY(@venueIds))
              AND (@date::date IS NULL OR b.business_date = @date)
              AND (@from::date IS NULL OR b.business_date >= @from)
              AND (@to::date IS NULL OR b.business_date <= @to)
              AND (NOT @hasStatuses OR b.status = ANY(@statuses))
              AND (@source::text IS NULL OR b.source = @source)
              AND (@customerId::uuid IS NULL OR b.customer_id = @customerId)
              AND (@resourceId::uuid IS NULL OR EXISTS (
                    SELECT 1 FROM booking_resources br WHERE br.booking_id = b.id AND br.resource_id = @resourceId))
              AND (@sectionId::uuid IS NULL OR EXISTS (
                    SELECT 1 FROM booking_resources br JOIN bookable_resources r ON r.id = br.resource_id
                    WHERE br.booking_id = b.id AND r.section_id = @sectionId))
              AND (@q::text IS NULL
                   OR b.reference ILIKE @qLike
                   OR c.email::text ILIKE @qLike
                   OR (c.first_name || ' ' || coalesce(c.last_name, '')) ILIKE @qLike
                   OR (@qDigits::text IS NOT NULL AND c.phone_normalized LIKE @qDigits))
            """;

        var q = string.IsNullOrWhiteSpace(query.Q) ? null : query.Q.Trim();
        var digits = q is null ? null : new string(q.Where(char.IsAsciiDigit).ToArray());
        var statuses = query.Statuses?.ToArray() ?? [];
        var from = SqlFrom(filter);
        await using var grid = await db.QueryMultipleAsync(
            $"""
            SELECT count(*) {from};
            {SelectBooking} {filter} ORDER BY {(query.NewestFirst ? "b.starts_at DESC, b.reference DESC" : "b.starts_at, b.reference")}
            LIMIT @PageSize OFFSET @Offset;
            """,
            new
            {
                tenantId,
                allVenues = venueIds is null,
                venueIds = venueIds?.ToArray() ?? [],
                date = query.Date,
                from = query.From,
                to = query.To,
                hasStatuses = statuses.Length > 0,
                statuses,
                source = query.Source,
                customerId = query.CustomerId,
                resourceId = query.ResourceId,
                sectionId = query.SectionId,
                q,
                qLike = SqlText.Contains(q),
                qDigits = digits is { Length: >= 3 } ? $"%{digits}%" : null,
                query.Page.PageSize,
                query.Page.Offset,
            },
            cancellationToken);

        var total = await grid.ReadSingleAsync<long>();
        var rows = (await grid.ReadAsync<BookingRow>()).ToList();
        var resources = await LoadResourcesAsync(tenantId, rows.Select(r => r.Id).ToArray(), cancellationToken);
        return new PagedResult<BookingRecord>(rows.Select(r => Map(r, resources)).ToList(), query.Page.Page, query.Page.PageSize, total);
    }

    public async Task ReplaceResourcesAsync(Guid tenantId, Guid venueId, Guid bookingId, IReadOnlyList<Guid> resourceIds, Guid? combinationId, CancellationToken cancellationToken)
    {
        await db.ExecuteAsync("DELETE FROM booking_resources WHERE tenant_id = @tenantId AND booking_id = @bookingId", new { tenantId, bookingId }, cancellationToken);
        await db.ExecuteAsync(
            """
            INSERT INTO booking_resources (tenant_id, venue_id, booking_id, resource_id, combination_id)
            SELECT @tenantId, @venueId, @bookingId, r, @combinationId FROM unnest(@resourceIds) AS r
            """,
            new { tenantId, venueId, bookingId, resourceIds = resourceIds.ToArray(), combinationId },
            cancellationToken);
    }

    public Task UpdateStatusAsync(Guid tenantId, Guid bookingId, string status, string? cancellationReason, DateTime now, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            """
            UPDATE bookings SET
                status = @status,
                version = version + 1,
                updated_at = @now,
                confirmed_at = CASE WHEN @status = 'CONFIRMED' THEN @now ELSE confirmed_at END,
                checked_in_at = CASE WHEN @status = 'CHECKED_IN' THEN @now ELSE checked_in_at END,
                seated_at = CASE WHEN @status = 'SEATED' THEN @now ELSE seated_at END,
                completed_at = CASE WHEN @status = 'COMPLETED' THEN @now ELSE completed_at END,
                cancelled_at = CASE WHEN @status = 'CANCELLED' THEN @now ELSE cancelled_at END,
                no_show_at = CASE WHEN @status = 'NO_SHOW' THEN @now ELSE no_show_at END,
                cancellation_reason = CASE WHEN @status = 'CANCELLED' THEN @cancellationReason ELSE cancellation_reason END,
                expires_at = CASE WHEN @status = 'HELD' THEN expires_at END
            WHERE tenant_id = @tenantId AND id = @bookingId
            """,
            new { tenantId, bookingId, status, cancellationReason, now },
            cancellationToken);

    public Task UpdateDetailsAsync(Guid tenantId, Guid bookingId, int partySize, string? specialRequests, string? internalNotes, Guid? customerId, DateTime now, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            """
            UPDATE bookings SET party_size = @partySize, special_requests = @specialRequests, internal_notes = @internalNotes,
                                customer_id = @customerId, version = version + 1, updated_at = @now
            WHERE tenant_id = @tenantId AND id = @bookingId
            """,
            new { tenantId, bookingId, partySize, specialRequests, internalNotes, customerId, now },
            cancellationToken);

    public Task UpdateTimesAsync(Guid tenantId, Guid bookingId, DateTime startsAt, DateTime endsAt, DateOnly businessDate, DateTime now, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            """
            UPDATE bookings SET starts_at = @startsAt, ends_at = @endsAt, business_date = @businessDate, version = version + 1, updated_at = @now
            WHERE tenant_id = @tenantId AND id = @bookingId
            """,
            new { tenantId, bookingId, startsAt, endsAt, businessDate, now },
            cancellationToken);

    public Task WriteStatusHistoryAsync(StatusHistoryEntry entry, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            """
            INSERT INTO booking_status_history (tenant_id, venue_id, booking_id, from_status, to_status, source, reason, changed_by_user_id)
            VALUES (@TenantId, @VenueId, @BookingId, @FromStatus, @ToStatus, @Source, @Reason, @UserId)
            """,
            entry,
            cancellationToken);

    public Task WriteEventAsync(BookingEventEntry entry, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            """
            INSERT INTO booking_events (tenant_id, venue_id, booking_id, event_type, data, actor_user_id)
            VALUES (@TenantId, @VenueId, @BookingId, @EventType, @Data::jsonb, @ActorUserId)
            """,
            new
            {
                entry.TenantId,
                entry.VenueId,
                entry.BookingId,
                entry.EventType,
                Data = JsonSerializer.Serialize(entry.Data, entry.Data.GetType(), JsonOptions),
                entry.ActorUserId,
            },
            cancellationToken);

    public async Task<BookingHistoryDto> GetHistoryAsync(Guid tenantId, Guid bookingId, CancellationToken cancellationToken)
    {
        var statuses = await db.QueryAsync<BookingStatusHistoryDto>(
            """
            SELECT id, from_status, to_status, source, reason, changed_by_user_id, changed_at
            FROM booking_status_history WHERE tenant_id = @tenantId AND booking_id = @bookingId ORDER BY changed_at, id
            """,
            new { tenantId, bookingId }, cancellationToken);
        var events = await db.QueryAsync<EventRow>(
            """
            SELECT id, event_type, data::text AS data, actor_user_id, occurred_at
            FROM booking_events WHERE tenant_id = @tenantId AND booking_id = @bookingId ORDER BY occurred_at, id
            """,
            new { tenantId, bookingId }, cancellationToken);
        return new BookingHistoryDto(bookingId, statuses,
            events.Select(e => new BookingEventDto(e.Id, e.EventType, JsonDocument.Parse(e.Data).RootElement.Clone(), e.ActorUserId, e.OccurredAt)).ToList());
    }

    private async Task<ILookup<Guid, BookingResourceDto>> LoadResourcesAsync(Guid tenantId, Guid[] bookingIds, CancellationToken cancellationToken)
    {
        if (bookingIds.Length == 0)
        {
            return Array.Empty<ResourceRow>().ToLookup(r => r.BookingId, r => new BookingResourceDto(r.ResourceId, r.Name, r.Code, r.CombinationId));
        }

        var rows = await db.QueryAsync<ResourceRow>(
            """
            SELECT br.booking_id, br.resource_id, r.name, r.code, br.combination_id
            FROM booking_resources br JOIN bookable_resources r ON r.id = br.resource_id
            WHERE br.tenant_id = @tenantId AND br.booking_id = ANY(@bookingIds)
            ORDER BY r.sort_order, r.name, r.id
            """,
            new { tenantId, bookingIds }, cancellationToken);
        return rows.ToLookup(r => r.BookingId, r => new BookingResourceDto(r.ResourceId, r.Name, r.Code, r.CombinationId));
    }

    private static BookingRecord Map(BookingRow r, ILookup<Guid, BookingResourceDto> resources) => new(
        r.Id, r.VenueId, r.TimeZone, r.CustomerId, r.Reference, r.Status, r.Source, r.PartySize, r.StartsAt, r.EndsAt, r.BusinessDate,
        r.SpecialRequests, r.InternalNotes, r.ExpiresAt, r.CreatedByUserId, r.Version, r.ConfirmedAt, r.CheckedInAt, r.SeatedAt,
        r.CompletedAt, r.CancelledAt, r.NoShowAt, r.CancellationReason, r.CreatedAt, r.UpdatedAt,
        r.CustomerId is { } customerId && r.CustomerFirstName is not null
            ? new BookingCustomerDto(customerId, r.CustomerFirstName, r.CustomerLastName, r.CustomerEmail, r.CustomerPhone)
            : null,
        resources[r.Id].ToList());

    private static string SqlFrom(string filter) =>
        $"FROM bookings b LEFT JOIN customers c ON c.tenant_id = b.tenant_id AND c.id = b.customer_id {filter}";
    private sealed class BookingRow
    {
        public Guid Id { get; init; }
        public Guid VenueId { get; init; }
        public string TimeZone { get; init; } = string.Empty;
        public Guid? CustomerId { get; init; }
        public string Reference { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string Source { get; init; } = string.Empty;
        public int PartySize { get; init; }
        public DateTime StartsAt { get; init; }
        public DateTime EndsAt { get; init; }
        public DateOnly BusinessDate { get; init; }
        public string? SpecialRequests { get; init; }
        public string? InternalNotes { get; init; }
        public DateTime? ExpiresAt { get; init; }
        public Guid? CreatedByUserId { get; init; }
        public int Version { get; init; }
        public DateTime? ConfirmedAt { get; init; }
        public DateTime? CheckedInAt { get; init; }
        public DateTime? SeatedAt { get; init; }
        public DateTime? CompletedAt { get; init; }
        public DateTime? CancelledAt { get; init; }
        public DateTime? NoShowAt { get; init; }
        public string? CancellationReason { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
        public string? CustomerFirstName { get; init; }
        public string? CustomerLastName { get; init; }
        public string? CustomerEmail { get; init; }
        public string? CustomerPhone { get; init; }
    }

    private sealed class LockRow
    {
        public Guid VenueId { get; init; }
        public string Status { get; init; } = string.Empty;
        public int Version { get; init; }
    }

    private sealed class TokenRow
    {
        public Guid TenantId { get; init; }
        public Guid Id { get; init; }
    }

    private sealed class ResourceRow
    {
        public Guid BookingId { get; init; }
        public Guid ResourceId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Code { get; init; } = string.Empty;
        public Guid? CombinationId { get; init; }
    }

    private sealed class EventRow
    {
        public Guid Id { get; init; }
        public string EventType { get; init; } = string.Empty;
        public string Data { get; init; } = "{}";
        public Guid? ActorUserId { get; init; }
        public DateTime OccurredAt { get; init; }
    }
}

public sealed class BookingCustomerRepository(DbSession db) : IBookingCustomerRepository
{
    public Task<BookingCustomerDto?> GetAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken) =>
        db.QuerySingleOrDefaultAsync<BookingCustomerDto>(
            """
            SELECT id, first_name, last_name, email::text AS email, phone FROM customers
            WHERE tenant_id = @tenantId AND id = @customerId AND anonymized_at IS NULL
            """,
            new { tenantId, customerId }, cancellationToken);

    public Task<Guid?> FindByEmailAsync(Guid tenantId, string email, CancellationToken cancellationToken) =>
        db.QuerySingleOrDefaultAsync<Guid?>(
            """
            SELECT id FROM customers WHERE tenant_id = @tenantId AND email = @email::citext AND anonymized_at IS NULL
            ORDER BY created_at, id LIMIT 1
            """,
            new { tenantId, email }, cancellationToken);

    public Task<Guid> CreateAsync(Guid tenantId, NewCustomer customer, CancellationToken cancellationToken) =>
        db.QuerySingleAsync<Guid>(
            """
            INSERT INTO customers (tenant_id, first_name, last_name, email, phone, phone_normalized, marketing_consent)
            VALUES (@tenantId, @FirstName, @LastName, @Email, @Phone, @PhoneNormalized, @MarketingConsent) RETURNING id
            """,
            new { tenantId, customer.FirstName, customer.LastName, customer.Email, customer.Phone, customer.PhoneNormalized, customer.MarketingConsent },
            cancellationToken);
}

public sealed class AllocatableResourceRepository(DbSession db) : IAllocatableResourceRepository
{
    public Task<IReadOnlyList<AllocatableResource>> GetManyAsync(Guid tenantId, IReadOnlyCollection<Guid> resourceIds, CancellationToken cancellationToken) =>
        db.QueryAsync<AllocatableResource>(
            """
            SELECT id, venue_id, name, min_capacity, capacity, is_active, section_id, sort_order
            FROM bookable_resources WHERE tenant_id = @tenantId AND id = ANY(@resourceIds)
            """,
            new { tenantId, resourceIds = resourceIds.ToArray() }, cancellationToken);
}
