using BookingPlatform.Application.Venues;
using BookingPlatform.Infrastructure.Persistence;

namespace BookingPlatform.Infrastructure.Venues;

public sealed class VenueRepository(DbSession db) : IVenueRepository
{
    private const string VenueColumns = """
        id, name, slug, time_zone, status, description, address_line1, address_line2, city, region, postal_code,
        country_code, phone, email, slot_interval_minutes, default_duration_minutes, min_duration_minutes,
        max_duration_minutes, turnover_buffer_minutes, min_party_size, max_party_size, min_lead_time_minutes,
        max_advance_days, hold_ttl_seconds, auto_confirm_public_bookings, public_booking_enabled,
        public_cancellation_cutoff_minutes, created_at, updated_at
        """;

    private const string SectionColumns = "id, venue_id, name, description, sort_order, is_active";

    private const string SpecialColumns = "id, date, name, is_closed, open_time, close_time";

    public async Task<IReadOnlyList<VenueDto>> ListAsync(Guid tenantId, IReadOnlyCollection<Guid>? venueIds, CancellationToken cancellationToken)
    {
        var rows = await db.QueryAsync<VenueRow>(
            $"""
            SELECT {VenueColumns} FROM venues
            WHERE tenant_id = @tenantId AND (@all OR id = ANY(@venueIds))
            ORDER BY name, id
            """,
            new { tenantId, all = venueIds is null, venueIds = venueIds?.ToArray() ?? [] },
            cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task<VenueDto?> GetAsync(Guid tenantId, Guid venueId, CancellationToken cancellationToken)
    {
        var row = await db.QuerySingleOrDefaultAsync<VenueRow>(
            $"SELECT {VenueColumns} FROM venues WHERE tenant_id = @tenantId AND id = @venueId",
            new { tenantId, venueId },
            cancellationToken);
        return row is null ? null : Map(row);
    }

    public Task<VenueRef?> GetRefAsync(Guid tenantId, Guid venueId, CancellationToken cancellationToken) =>
        db.QuerySingleOrDefaultAsync<VenueRef>(
            "SELECT id, time_zone, status FROM venues WHERE tenant_id = @tenantId AND id = @venueId",
            new { tenantId, venueId },
            cancellationToken);

    public async Task<bool> SlugExistsAsync(Guid tenantId, string slug, CancellationToken cancellationToken) =>
        await db.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM venues WHERE tenant_id = @tenantId AND slug = @slug)",
            new { tenantId, slug },
            cancellationToken);

    public Task<int> CountAsync(Guid tenantId, CancellationToken cancellationToken) =>
        db.QuerySingleAsync<int>("SELECT count(*)::int FROM venues WHERE tenant_id = @tenantId", new { tenantId }, cancellationToken);

    public Task<Guid> CreateAsync(Guid tenantId, CreateVenueRequest request, VenueBookingRules rules, CancellationToken cancellationToken) =>
        db.QuerySingleAsync<Guid>(
            """
            INSERT INTO venues (tenant_id, name, slug, time_zone, description, address_line1, address_line2, city, region,
                                postal_code, country_code, phone, email, slot_interval_minutes, default_duration_minutes,
                                min_duration_minutes, max_duration_minutes, turnover_buffer_minutes, min_party_size,
                                max_party_size, min_lead_time_minutes, max_advance_days, hold_ttl_seconds,
                                auto_confirm_public_bookings, public_booking_enabled, public_cancellation_cutoff_minutes)
            VALUES (@tenantId, @Name, @Slug, @TimeZone, @Description, @AddressLine1, @AddressLine2, @City, @Region,
                    @PostalCode, @CountryCode, @Phone, @Email, @SlotIntervalMinutes, @DefaultDurationMinutes,
                    @MinDurationMinutes, @MaxDurationMinutes, @TurnoverBufferMinutes, @MinPartySize, @MaxPartySize,
                    @MinLeadTimeMinutes, @MaxAdvanceDays, @HoldTtlSeconds, @AutoConfirmPublicBookings,
                    @PublicBookingEnabled, @PublicCancellationCutoffMinutes)
            RETURNING id
            """,
            new
            {
                tenantId, request.Name, request.Slug, request.TimeZone, request.Description, request.AddressLine1,
                request.AddressLine2, request.City, request.Region, request.PostalCode, request.CountryCode,
                request.Phone, request.Email, rules.SlotIntervalMinutes, rules.DefaultDurationMinutes,
                rules.MinDurationMinutes, rules.MaxDurationMinutes, rules.TurnoverBufferMinutes, rules.MinPartySize,
                rules.MaxPartySize, rules.MinLeadTimeMinutes, rules.MaxAdvanceDays, rules.HoldTtlSeconds,
                rules.AutoConfirmPublicBookings, rules.PublicBookingEnabled, rules.PublicCancellationCutoffMinutes,
            },
            cancellationToken);

    public Task UpdateAsync(Guid tenantId, Guid venueId, UpdateVenueRequest request, CancellationToken cancellationToken)
    {
        var rules = request.Rules;
        return db.ExecuteAsync(
            """
            UPDATE venues SET
                name = @Name, time_zone = @TimeZone, status = @Status, description = @Description,
                address_line1 = @AddressLine1, address_line2 = @AddressLine2, city = @City, region = @Region,
                postal_code = @PostalCode, country_code = @CountryCode, phone = @Phone, email = @Email,
                slot_interval_minutes = @SlotIntervalMinutes, default_duration_minutes = @DefaultDurationMinutes,
                min_duration_minutes = @MinDurationMinutes, max_duration_minutes = @MaxDurationMinutes,
                turnover_buffer_minutes = @TurnoverBufferMinutes, min_party_size = @MinPartySize,
                max_party_size = @MaxPartySize, min_lead_time_minutes = @MinLeadTimeMinutes,
                max_advance_days = @MaxAdvanceDays, hold_ttl_seconds = @HoldTtlSeconds,
                auto_confirm_public_bookings = @AutoConfirmPublicBookings, public_booking_enabled = @PublicBookingEnabled,
                public_cancellation_cutoff_minutes = @PublicCancellationCutoffMinutes, updated_at = now()
            WHERE tenant_id = @tenantId AND id = @venueId
            """,
            new
            {
                tenantId, venueId, request.Name, request.TimeZone, request.Status, request.Description,
                request.AddressLine1, request.AddressLine2, request.City, request.Region, request.PostalCode,
                request.CountryCode, request.Phone, request.Email, rules.SlotIntervalMinutes,
                rules.DefaultDurationMinutes, rules.MinDurationMinutes, rules.MaxDurationMinutes,
                rules.TurnoverBufferMinutes, rules.MinPartySize, rules.MaxPartySize, rules.MinLeadTimeMinutes,
                rules.MaxAdvanceDays, rules.HoldTtlSeconds, rules.AutoConfirmPublicBookings, rules.PublicBookingEnabled,
                rules.PublicCancellationCutoffMinutes,
            },
            cancellationToken);
    }

    public Task LockAsync(Guid tenantId, Guid venueId, CancellationToken cancellationToken) =>
        db.ExecuteAsync("SELECT 1 FROM venues WHERE tenant_id = @tenantId AND id = @venueId FOR UPDATE", new { tenantId, venueId }, cancellationToken);

    public Task<IReadOnlyList<SectionDto>> ListSectionsAsync(Guid tenantId, Guid venueId, CancellationToken cancellationToken) =>
        db.QueryAsync<SectionDto>(
            $"SELECT {SectionColumns} FROM venue_sections WHERE tenant_id = @tenantId AND venue_id = @venueId ORDER BY sort_order, name",
            new { tenantId, venueId },
            cancellationToken);

    public Task<SectionDto?> GetSectionAsync(Guid tenantId, Guid venueId, Guid sectionId, CancellationToken cancellationToken) =>
        db.QuerySingleOrDefaultAsync<SectionDto>(
            $"SELECT {SectionColumns} FROM venue_sections WHERE tenant_id = @tenantId AND venue_id = @venueId AND id = @sectionId",
            new { tenantId, venueId, sectionId },
            cancellationToken);

    public async Task<bool> SectionNameExistsAsync(Guid tenantId, Guid venueId, string name, Guid? exceptId, CancellationToken cancellationToken) =>
        await db.ExecuteScalarAsync<bool>(
            """
            SELECT EXISTS (SELECT 1 FROM venue_sections
                           WHERE tenant_id = @tenantId AND venue_id = @venueId AND lower(name) = lower(@name)
                             AND (@exceptId::uuid IS NULL OR id <> @exceptId))
            """,
            new { tenantId, venueId, name, exceptId },
            cancellationToken);

    public Task<Guid> CreateSectionAsync(Guid tenantId, Guid venueId, SectionRequest request, CancellationToken cancellationToken) =>
        db.QuerySingleAsync<Guid>(
            """
            INSERT INTO venue_sections (tenant_id, venue_id, name, description, sort_order, is_active)
            VALUES (@tenantId, @venueId, @Name, @Description, @SortOrder, @IsActive)
            RETURNING id
            """,
            new { tenantId, venueId, request.Name, request.Description, request.SortOrder, request.IsActive },
            cancellationToken);

    public Task UpdateSectionAsync(Guid tenantId, Guid venueId, Guid sectionId, SectionRequest request, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            """
            UPDATE venue_sections
            SET name = @Name, description = @Description, sort_order = @SortOrder, is_active = @IsActive, updated_at = now()
            WHERE tenant_id = @tenantId AND venue_id = @venueId AND id = @sectionId
            """,
            new { tenantId, venueId, sectionId, request.Name, request.Description, request.SortOrder, request.IsActive },
            cancellationToken);

    public async Task<DeleteResult> DeleteOrDeactivateSectionAsync(Guid tenantId, Guid venueId, Guid sectionId, CancellationToken cancellationToken)
    {
        var parameters = new { tenantId, venueId, sectionId };
        if (await db.TryDeleteAsync(
                "DELETE FROM venue_sections WHERE tenant_id = @tenantId AND venue_id = @venueId AND id = @sectionId", parameters, cancellationToken))
        {
            return new DeleteResult(Deleted: true, Deactivated: false);
        }

        await db.ExecuteAsync(
            "UPDATE venue_sections SET is_active = false, updated_at = now() WHERE tenant_id = @tenantId AND venue_id = @venueId AND id = @sectionId",
            parameters,
            cancellationToken);
        return new DeleteResult(Deleted: false, Deactivated: true);
    }

    public async Task<IReadOnlyList<OperatingHoursPeriod>> ListOperatingHoursAsync(Guid tenantId, Guid venueId, CancellationToken cancellationToken)
    {
        var rows = await db.QueryAsync<HoursRow>(
            """
            SELECT day_of_week, open_time, close_time FROM operating_hours
            WHERE tenant_id = @tenantId AND venue_id = @venueId
            ORDER BY day_of_week, open_time
            """,
            new { tenantId, venueId },
            cancellationToken);
        return rows.Select(r => new OperatingHoursPeriod(r.DayOfWeek, r.OpenTime, r.CloseTime)).ToList();
    }

    public async Task ReplaceOperatingHoursAsync(Guid tenantId, Guid venueId, IReadOnlyList<OperatingHoursPeriod> periods, CancellationToken cancellationToken)
    {
        await db.ExecuteAsync("DELETE FROM operating_hours WHERE tenant_id = @tenantId AND venue_id = @venueId", new { tenantId, venueId }, cancellationToken);
        if (periods.Count == 0)
        {
            return;
        }

        await db.ExecuteAsync(
            """
            INSERT INTO operating_hours (tenant_id, venue_id, day_of_week, open_time, close_time)
            SELECT @tenantId, @venueId, d, o, c
            FROM unnest(@days, @opens, @closes) AS p(d, o, c)
            """,
            new
            {
                tenantId,
                venueId,
                days = periods.Select(p => (short)p.DayOfWeek).ToArray(),
                opens = periods.Select(p => p.OpenTime).ToArray(),
                closes = periods.Select(p => p.CloseTime).ToArray(),
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<SpecialHoursDto>> ListSpecialHoursAsync(Guid tenantId, Guid venueId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var rows = await db.QueryAsync<SpecialRow>(
            $"""
            SELECT {SpecialColumns} FROM special_operating_hours
            WHERE tenant_id = @tenantId AND venue_id = @venueId
              AND (@from::date IS NULL OR date >= @from) AND (@to::date IS NULL OR date <= @to)
            ORDER BY date, open_time NULLS FIRST
            """,
            new { tenantId, venueId, from, to },
            cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task<SpecialHoursDto?> GetSpecialHoursAsync(Guid tenantId, Guid venueId, Guid id, CancellationToken cancellationToken)
    {
        var row = await db.QuerySingleOrDefaultAsync<SpecialRow>(
            $"SELECT {SpecialColumns} FROM special_operating_hours WHERE tenant_id = @tenantId AND venue_id = @venueId AND id = @id",
            new { tenantId, venueId, id },
            cancellationToken);
        return row is null ? null : Map(row);
    }

    private static SpecialHoursDto Map(SpecialRow r) => new(r.Id, r.Date, r.Name, r.IsClosed, r.OpenTime, r.CloseTime);

    public Task<Guid> CreateSpecialHoursAsync(Guid tenantId, Guid venueId, SpecialHoursRequest request, CancellationToken cancellationToken) =>
        db.QuerySingleAsync<Guid>(
            """
            INSERT INTO special_operating_hours (tenant_id, venue_id, date, name, is_closed, open_time, close_time)
            VALUES (@tenantId, @venueId, @Date, @Name, @IsClosed, @OpenTime, @CloseTime)
            RETURNING id
            """,
            new { tenantId, venueId, request.Date, request.Name, request.IsClosed, request.OpenTime, request.CloseTime },
            cancellationToken);

    public Task UpdateSpecialHoursAsync(Guid tenantId, Guid venueId, Guid id, SpecialHoursRequest request, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            """
            UPDATE special_operating_hours
            SET date = @Date, name = @Name, is_closed = @IsClosed, open_time = @OpenTime, close_time = @CloseTime, updated_at = now()
            WHERE tenant_id = @tenantId AND venue_id = @venueId AND id = @id
            """,
            new { tenantId, venueId, id, request.Date, request.Name, request.IsClosed, request.OpenTime, request.CloseTime },
            cancellationToken);

    public Task DeleteSpecialHoursAsync(Guid tenantId, Guid venueId, Guid id, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            "DELETE FROM special_operating_hours WHERE tenant_id = @tenantId AND venue_id = @venueId AND id = @id",
            new { tenantId, venueId, id },
            cancellationToken);

    private static VenueDto Map(VenueRow r) => new(
        r.Id, r.Name, r.Slug, r.TimeZone, r.Status, r.Description, r.AddressLine1, r.AddressLine2, r.City, r.Region,
        r.PostalCode, r.CountryCode, r.Phone, r.Email,
        new VenueBookingRules(r.SlotIntervalMinutes, r.DefaultDurationMinutes, r.MinDurationMinutes, r.MaxDurationMinutes,
            r.TurnoverBufferMinutes, r.MinPartySize, r.MaxPartySize, r.MinLeadTimeMinutes, r.MaxAdvanceDays,
            r.HoldTtlSeconds, r.AutoConfirmPublicBookings, r.PublicBookingEnabled, r.PublicCancellationCutoffMinutes),
        r.CreatedAt, r.UpdatedAt);

    private sealed class VenueRow
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Slug { get; init; } = string.Empty;
        public string TimeZone { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string? Description { get; init; }
        public string? AddressLine1 { get; init; }
        public string? AddressLine2 { get; init; }
        public string? City { get; init; }
        public string? Region { get; init; }
        public string? PostalCode { get; init; }
        public string? CountryCode { get; init; }
        public string? Phone { get; init; }
        public string? Email { get; init; }
        public int SlotIntervalMinutes { get; init; }
        public int DefaultDurationMinutes { get; init; }
        public int MinDurationMinutes { get; init; }
        public int MaxDurationMinutes { get; init; }
        public int TurnoverBufferMinutes { get; init; }
        public int MinPartySize { get; init; }
        public int MaxPartySize { get; init; }
        public int MinLeadTimeMinutes { get; init; }
        public int MaxAdvanceDays { get; init; }
        public int HoldTtlSeconds { get; init; }
        public bool AutoConfirmPublicBookings { get; init; }
        public bool PublicBookingEnabled { get; init; }
        public int PublicCancellationCutoffMinutes { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
    }

    private sealed class SpecialRow
    {
        public Guid Id { get; init; }
        public DateOnly Date { get; init; }
        public string? Name { get; init; }
        public bool IsClosed { get; init; }
        public TimeOnly? OpenTime { get; init; }
        public TimeOnly? CloseTime { get; init; }
    }

    private sealed class HoursRow
    {
        public short DayOfWeek { get; init; }
        public TimeOnly OpenTime { get; init; }
        public TimeOnly CloseTime { get; init; }
    }
}

public sealed class VenueAccessRepository(DbSession db) : IVenueAccessRepository
{
    public async Task<bool> IsAssignedAsync(Guid tenantId, Guid tenantUserId, Guid venueId, CancellationToken cancellationToken) =>
        await db.ExecuteScalarAsync<bool>(
            """
            SELECT EXISTS (SELECT 1 FROM tenant_user_venues
                           WHERE tenant_id = @tenantId AND tenant_user_id = @tenantUserId AND venue_id = @venueId)
            """,
            new { tenantId, tenantUserId, venueId },
            cancellationToken);

    public Task<IReadOnlyList<Guid>> ListAssignedAsync(Guid tenantId, Guid tenantUserId, CancellationToken cancellationToken) =>
        db.QueryAsync<Guid>(
            "SELECT venue_id FROM tenant_user_venues WHERE tenant_id = @tenantId AND tenant_user_id = @tenantUserId ORDER BY venue_id",
            new { tenantId, tenantUserId },
            cancellationToken);

    public Task<IReadOnlyList<Guid>> ListAllVenueIdsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        db.QueryAsync<Guid>("SELECT id FROM venues WHERE tenant_id = @tenantId ORDER BY id", new { tenantId }, cancellationToken);

    public async Task ReplaceAssignmentsAsync(Guid tenantId, Guid tenantUserId, IReadOnlyCollection<Guid> venueIds, CancellationToken cancellationToken)
    {
        await db.ExecuteAsync(
            "DELETE FROM tenant_user_venues WHERE tenant_id = @tenantId AND tenant_user_id = @tenantUserId",
            new { tenantId, tenantUserId },
            cancellationToken);
        if (venueIds.Count == 0)
        {
            return;
        }

        await db.ExecuteAsync(
            """
            INSERT INTO tenant_user_venues (tenant_id, tenant_user_id, venue_id)
            SELECT @tenantId, @tenantUserId, v FROM unnest(@venueIds) AS v
            """,
            new { tenantId, tenantUserId, venueIds = venueIds.ToArray() },
            cancellationToken);
    }
}
