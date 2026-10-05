namespace BookingPlatform.IntegrationTests.Infrastructure;

/// <summary>Direct SQL seeding of bookings, holds and blocks with their allocations (until the booking API exists).</summary>
public static class ClaimSeeder
{
    public static Task<Guid> SeedBookingAsync(this TestData data, Guid tenantId, Guid venueId, Guid resourceId,
        DateTime start, DateTime end, bool active = true) =>
        data.ScalarAsync<Guid>(
            """
            WITH b AS (
                INSERT INTO bookings (tenant_id, venue_id, reference, status, source, party_size, starts_at, ends_at,
                                      business_date, cancelled_at)
                VALUES (@tenantId, @venueId, 'BK-' || to_char(@start AT TIME ZONE 'UTC', 'YYYYMMDD') || '-'
                            || lpad(nextval('booking_reference_seq')::text, 6, '0'),
                        CASE WHEN @active THEN 'CONFIRMED' ELSE 'CANCELLED' END, 'STAFF', 2, @start, @end,
                        (@start AT TIME ZONE 'UTC')::date, CASE WHEN @active THEN NULL ELSE now() END)
                RETURNING id),
            r AS (
                INSERT INTO booking_resources (tenant_id, venue_id, booking_id, resource_id)
                SELECT @tenantId, @venueId, id, @resourceId FROM b)
            INSERT INTO booking_allocations (tenant_id, venue_id, resource_id, allocation_type, booking_id, starts_at, ends_at,
                                             is_active, released_at, release_reason)
            SELECT @tenantId, @venueId, @resourceId, 'BOOKING', id, @start, @end, @active,
                   CASE WHEN @active THEN NULL ELSE now() END, CASE WHEN @active THEN NULL ELSE 'CANCELLED' END
            FROM b
            RETURNING booking_id
            """,
            new { tenantId, venueId, resourceId, start, end, active });

    public static Task<Guid> SeedHoldAsync(this TestData data, Guid tenantId, Guid venueId, Guid resourceId,
        DateTime start, DateTime end, DateTime expiresAt) =>
        data.ScalarAsync<Guid>(
            """
            WITH h AS (
                INSERT INTO resource_holds (tenant_id, venue_id, owner_type, owner_reference, token_hash, party_size,
                                            starts_at, ends_at, expires_at)
                VALUES (@tenantId, @venueId, 'USER', 'test', sha256(gen_random_uuid()::text::bytea), 2, @start, @end, @expiresAt)
                RETURNING id)
            INSERT INTO booking_allocations (tenant_id, venue_id, resource_id, allocation_type, hold_id, starts_at, ends_at, expires_at)
            SELECT @tenantId, @venueId, @resourceId, 'HOLD', id, @start, @end, @expiresAt FROM h
            RETURNING hold_id
            """,
            new { tenantId, venueId, resourceId, start, end, expiresAt });

    public static Task<Guid> SeedBlockAsync(this TestData data, Guid tenantId, Guid venueId, Guid resourceId, DateTime start, DateTime end) =>
        data.ScalarAsync<Guid>(
            """
            WITH k AS (
                INSERT INTO resource_blocks (tenant_id, venue_id, reason, starts_at, ends_at)
                VALUES (@tenantId, @venueId, 'MAINTENANCE', @start, @end)
                RETURNING id)
            INSERT INTO booking_allocations (tenant_id, venue_id, resource_id, allocation_type, block_id, starts_at, ends_at)
            SELECT @tenantId, @venueId, @resourceId, 'BLOCK', id, @start, @end FROM k
            RETURNING block_id
            """,
            new { tenantId, venueId, resourceId, start, end });
}
