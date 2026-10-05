using Npgsql;

namespace BookingPlatform.Migrations;

/// <summary>Thin parameterised command helper for the dev seeder.</summary>
internal sealed class SeedDb(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
{
    public async Task<object?> ScalarAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = Create(sql, parameters);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is DBNull ? null : result;
    }

    public async Task ExecuteAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = Create(sql, parameters);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private NpgsqlCommand Create(string sql, (string Name, object? Value)[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }
}

/// <summary>
/// Demo venues, sections, resources, hours and floor plans. A venue's contents are only seeded when the venue itself is
/// created, so re-running never overwrites edits made through the app.
/// </summary>
internal static class DemoVenues
{
    private sealed record Resource(string Code, string Name, string Type, string? Section, int Min, int Capacity, string Shape, int X, int Y, int W, int H);

    private sealed record Hours(int Day, TimeOnly Open, TimeOnly Close);

    public static async Task<Guid> EnsureRestaurantAsync(SeedDb db, Guid tenantId)
    {
        var (venueId, created) = await EnsureVenueAsync(db, tenantId, "Demo Restaurant", "demo-restaurant",
            "Modern bistro with a garden terrace.", slot: 15, defaultDuration: 120, minDuration: 60, maxDuration: 240, maxParty: 12);
        if (!created)
        {
            return venueId;
        }

        await EnsureTypesAsync(db, tenantId, ("TABLE", "Table"), ("BAR_SEAT", "Bar seat"), ("BOOTH", "Booth"));
        var sections = await CreateSectionsAsync(db, tenantId, venueId, "Main floor", "Terrace", "Bar");

        Resource[] resources =
        [
            new("T1", "Table 1", "TABLE", "Main floor", 1, 2, "ROUND", 80, 80, 80, 80),
            new("T2", "Table 2", "TABLE", "Main floor", 1, 2, "ROUND", 220, 80, 80, 80),
            new("T3", "Table 3", "TABLE", "Main floor", 2, 4, "RECT", 360, 70, 120, 100),
            new("T4", "Table 4", "TABLE", "Main floor", 2, 4, "RECT", 80, 240, 120, 100),
            new("T5", "Table 5", "TABLE", "Main floor", 2, 4, "RECT", 240, 240, 120, 100),
            new("T6", "Table 6", "TABLE", "Main floor", 4, 6, "RECT", 400, 240, 160, 100),
            new("T7", "Table 7", "TABLE", "Main floor", 4, 6, "RECT", 80, 400, 160, 100),
            new("T8", "Table 8", "BOOTH", "Main floor", 6, 8, "RECT", 280, 400, 200, 110),
            new("P1", "Terrace 1", "TABLE", "Terrace", 1, 2, "ROUND", 700, 80, 80, 80),
            new("P2", "Terrace 2", "TABLE", "Terrace", 2, 4, "ROUND", 840, 80, 100, 100),
            new("P3", "Terrace 3", "TABLE", "Terrace", 2, 4, "ROUND", 700, 240, 100, 100),
            new("P4", "Terrace 4", "TABLE", "Terrace", 4, 6, "RECT", 840, 240, 160, 100),
            new("B1", "Bar seat 1", "BAR_SEAT", "Bar", 1, 1, "ROUND", 700, 560, 50, 50),
            new("B2", "Bar seat 2", "BAR_SEAT", "Bar", 1, 1, "ROUND", 770, 560, 50, 50),
            new("B3", "Bar seat 3", "BAR_SEAT", "Bar", 1, 1, "ROUND", 840, 560, 50, 50),
            new("B4", "Bar seat 4", "BAR_SEAT", "Bar", 1, 1, "ROUND", 910, 560, 50, 50),
        ];

        var ids = await CreateResourcesAsync(db, tenantId, venueId, sections, resources);
        await CreateFloorPlanAsync(db, tenantId, venueId, "Main floor", 1100, 700, resources, ids);
        await CreateCombinationAsync(db, tenantId, venueId, "Tables 4 + 5", 5, 8, ids["T4"], ids["T5"]);

        var lunch = (new TimeOnly(12, 0), new TimeOnly(15, 0));
        var dinner = (new TimeOnly(18, 0), new TimeOnly(23, 0));
        var hours = Enumerable.Range(2, 6).SelectMany(day => new[]
        {
            new Hours(day, lunch.Item1, lunch.Item2),
            new Hours(day, dinner.Item1, dinner.Item2),
        });
        await CreateHoursAsync(db, tenantId, venueId, hours);
        return venueId;
    }

    public static async Task<Guid> EnsureClubAsync(SeedDb db, Guid tenantId)
    {
        var (venueId, created) = await EnsureVenueAsync(db, tenantId, "Demo Club", "demo-club",
            "Late-night club with VIP areas and booths.", slot: 30, defaultDuration: 240, minDuration: 120, maxDuration: 480, maxParty: 20);
        if (!created)
        {
            return venueId;
        }

        await EnsureTypesAsync(db, tenantId, ("VIP_AREA", "VIP area"), ("BOOTH", "Booth"));
        var sections = await CreateSectionsAsync(db, tenantId, venueId, "VIP lounge", "Dance floor");

        Resource[] resources =
        [
            new("VIP1", "VIP 1", "VIP_AREA", "VIP lounge", 6, 12, "RECT", 80, 80, 220, 140),
            new("VIP2", "VIP 2", "VIP_AREA", "VIP lounge", 6, 12, "RECT", 340, 80, 220, 140),
            new("VIP3", "VIP 3", "VIP_AREA", "VIP lounge", 8, 20, "RECT", 600, 80, 280, 160),
            new("BTH1", "Booth 1", "BOOTH", "Dance floor", 2, 6, "RECT", 80, 400, 140, 100),
            new("BTH2", "Booth 2", "BOOTH", "Dance floor", 2, 6, "RECT", 260, 400, 140, 100),
            new("BTH3", "Booth 3", "BOOTH", "Dance floor", 4, 8, "RECT", 440, 400, 160, 100),
            new("BTH4", "Booth 4", "BOOTH", "Dance floor", 4, 8, "RECT", 640, 400, 160, 100),
        ];

        var ids = await CreateResourcesAsync(db, tenantId, venueId, sections, resources);
        await CreateFloorPlanAsync(db, tenantId, venueId, "Club floor", 1000, 600, resources, ids);
        await CreateHoursAsync(db, tenantId, venueId,
            Enumerable.Range(3, 4).Select(day => new Hours(day, new TimeOnly(20, 0), new TimeOnly(4, 0))));
        return venueId;
    }

    public static async Task<Guid> EnsureWorkspaceAsync(SeedDb db, Guid tenantId)
    {
        var (venueId, created) = await EnsureVenueAsync(db, tenantId, "Demo Workspace", "demo-workspace",
            "Coworking space with meeting rooms and hot desks.", slot: 30, defaultDuration: 60, minDuration: 30, maxDuration: 480, maxParty: 16);
        if (!created)
        {
            return venueId;
        }

        await EnsureTypesAsync(db, tenantId, ("MEETING_ROOM", "Meeting room"), ("DESK", "Desk"));
        var sections = await CreateSectionsAsync(db, tenantId, venueId, "Level 1", "Level 2");

        Resource[] resources =
        [
            new("MR-A", "Meeting room A", "MEETING_ROOM", "Level 1", 1, 6, "RECT", 60, 60, 240, 160),
            new("MR-B", "Meeting room B", "MEETING_ROOM", "Level 1", 1, 10, "RECT", 340, 60, 300, 160),
            new("BOARD", "Boardroom", "MEETING_ROOM", "Level 2", 4, 16, "RECT", 680, 60, 360, 200),
            new("D1", "Desk 1", "DESK", "Level 1", 1, 1, "RECT", 60, 320, 90, 60),
            new("D2", "Desk 2", "DESK", "Level 1", 1, 1, "RECT", 170, 320, 90, 60),
            new("D3", "Desk 3", "DESK", "Level 1", 1, 1, "RECT", 280, 320, 90, 60),
            new("D4", "Desk 4", "DESK", "Level 1", 1, 1, "RECT", 60, 420, 90, 60),
            new("D5", "Desk 5", "DESK", "Level 1", 1, 1, "RECT", 170, 420, 90, 60),
            new("D6", "Desk 6", "DESK", "Level 1", 1, 1, "RECT", 280, 420, 90, 60),
        ];

        var ids = await CreateResourcesAsync(db, tenantId, venueId, sections, resources);
        await CreateFloorPlanAsync(db, tenantId, venueId, "Office", 1100, 600, resources, ids);
        await CreateHoursAsync(db, tenantId, venueId,
            Enumerable.Range(1, 5).Select(day => new Hours(day, new TimeOnly(8, 0), new TimeOnly(18, 0))));
        return venueId;
    }

    public static Task AssignAsync(SeedDb db, Guid tenantId, Guid tenantUserId, Guid venueId) =>
        db.ExecuteAsync(
            """
            INSERT INTO tenant_user_venues (tenant_id, tenant_user_id, venue_id) VALUES (@tenantId, @tenantUserId, @venueId)
            ON CONFLICT DO NOTHING
            """,
            ("tenantId", tenantId), ("tenantUserId", tenantUserId), ("venueId", venueId));

    private static async Task<(Guid Id, bool Created)> EnsureVenueAsync(
        SeedDb db, Guid tenantId, string name, string slug, string description, int slot, int defaultDuration, int minDuration,
        int maxDuration, int maxParty)
    {
        var inserted = await db.ScalarAsync(
            """
            INSERT INTO venues (tenant_id, name, slug, time_zone, description, city, country_code, slot_interval_minutes,
                                default_duration_minutes, min_duration_minutes, max_duration_minutes, max_party_size)
            VALUES (@tenantId, @name, @slug, 'Africa/Johannesburg', @description, 'Cape Town', 'ZA', @slot,
                    @defaultDuration, @minDuration, @maxDuration, @maxParty)
            ON CONFLICT (tenant_id, slug) DO NOTHING
            RETURNING id
            """,
            ("tenantId", tenantId), ("name", name), ("slug", slug), ("description", description), ("slot", slot),
            ("defaultDuration", defaultDuration), ("minDuration", minDuration), ("maxDuration", maxDuration), ("maxParty", maxParty));
        if (inserted is Guid id)
        {
            return (id, true);
        }

        var existing = await db.ScalarAsync("SELECT id FROM venues WHERE tenant_id = @tenantId AND slug = @slug", ("tenantId", tenantId), ("slug", slug));
        return ((Guid)existing!, false);
    }

    private static async Task EnsureTypesAsync(SeedDb db, Guid tenantId, params (string Code, string Name)[] types)
    {
        foreach (var (code, name) in types)
        {
            await db.ExecuteAsync(
                "INSERT INTO resource_types (tenant_id, code, name) VALUES (@tenantId, @code, @name) ON CONFLICT (tenant_id, code) DO NOTHING",
                ("tenantId", tenantId), ("code", code), ("name", name));
        }
    }

    private static async Task<Dictionary<string, Guid>> CreateSectionsAsync(SeedDb db, Guid tenantId, Guid venueId, params string[] names)
    {
        var ids = new Dictionary<string, Guid>();
        for (var i = 0; i < names.Length; i++)
        {
            ids[names[i]] = (Guid)(await db.ScalarAsync(
                "INSERT INTO venue_sections (tenant_id, venue_id, name, sort_order) VALUES (@tenantId, @venueId, @name, @sort) RETURNING id",
                ("tenantId", tenantId), ("venueId", venueId), ("name", names[i]), ("sort", i)))!;
        }

        return ids;
    }

    private static async Task<Dictionary<string, Guid>> CreateResourcesAsync(
        SeedDb db, Guid tenantId, Guid venueId, Dictionary<string, Guid> sections, IReadOnlyList<Resource> resources)
    {
        var ids = new Dictionary<string, Guid>();
        for (var i = 0; i < resources.Count; i++)
        {
            var r = resources[i];
            ids[r.Code] = (Guid)(await db.ScalarAsync(
                """
                INSERT INTO bookable_resources (tenant_id, venue_id, section_id, resource_type_id, name, code, min_capacity, capacity, sort_order)
                SELECT @tenantId, @venueId, @sectionId, t.id, @name, @code, @min, @capacity, @sort
                FROM resource_types t WHERE t.tenant_id = @tenantId AND t.code = @type
                RETURNING id
                """,
                ("tenantId", tenantId), ("venueId", venueId), ("sectionId", r.Section is null ? null : sections[r.Section]),
                ("name", r.Name), ("code", r.Code), ("min", r.Min), ("capacity", r.Capacity), ("sort", i), ("type", r.Type)))!;
        }

        return ids;
    }

    private static async Task CreateFloorPlanAsync(
        SeedDb db, Guid tenantId, Guid venueId, string name, int width, int height, IEnumerable<Resource> resources, Dictionary<string, Guid> ids)
    {
        var planId = (Guid)(await db.ScalarAsync(
            "INSERT INTO floor_plans (tenant_id, venue_id, name, width, height) VALUES (@tenantId, @venueId, @name, @width, @height) RETURNING id",
            ("tenantId", tenantId), ("venueId", venueId), ("name", name), ("width", width), ("height", height)))!;

        foreach (var r in resources)
        {
            await db.ExecuteAsync(
                """
                INSERT INTO floor_plan_resources (tenant_id, venue_id, floor_plan_id, resource_id, x, y, width, height, shape)
                VALUES (@tenantId, @venueId, @planId, @resourceId, @x, @y, @w, @h, @shape)
                """,
                ("tenantId", tenantId), ("venueId", venueId), ("planId", planId), ("resourceId", ids[r.Code]),
                ("x", (decimal)r.X), ("y", (decimal)r.Y), ("w", (decimal)r.W), ("h", (decimal)r.H), ("shape", r.Shape));
        }
    }

    private static async Task CreateCombinationAsync(SeedDb db, Guid tenantId, Guid venueId, string name, int min, int capacity, params Guid[] members)
    {
        var combinationId = (Guid)(await db.ScalarAsync(
            """
            INSERT INTO resource_combinations (tenant_id, venue_id, name, min_capacity, capacity)
            VALUES (@tenantId, @venueId, @name, @min, @capacity) RETURNING id
            """,
            ("tenantId", tenantId), ("venueId", venueId), ("name", name), ("min", min), ("capacity", capacity)))!;
        foreach (var resourceId in members)
        {
            await db.ExecuteAsync(
                """
                INSERT INTO resource_combination_members (tenant_id, venue_id, combination_id, resource_id)
                VALUES (@tenantId, @venueId, @combinationId, @resourceId)
                """,
                ("tenantId", tenantId), ("venueId", venueId), ("combinationId", combinationId), ("resourceId", resourceId));
        }
    }

    private static async Task CreateHoursAsync(SeedDb db, Guid tenantId, Guid venueId, IEnumerable<Hours> hours)
    {
        foreach (var h in hours)
        {
            await db.ExecuteAsync(
                "INSERT INTO operating_hours (tenant_id, venue_id, day_of_week, open_time, close_time) VALUES (@tenantId, @venueId, @day, @open, @close)",
                ("tenantId", tenantId), ("venueId", venueId), ("day", (short)h.Day), ("open", h.Open), ("close", h.Close));
        }
    }
}
