using BookingPlatform.IntegrationTests.Infrastructure;
using Dapper;
using Npgsql;

namespace BookingPlatform.IntegrationTests.Database;

/// <summary>The database is the final double-booking guarantee (DATABASE.md §4); these tests exercise it directly.</summary>
[Collection(InfrastructureCollection.Name)]
[Trait("Category", "Docker")]
public sealed class AllocationConstraintTests(InfrastructureFixture fixture)
{
    private static readonly DateTime Ten = new(2030, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    private sealed record Ids(Guid TenantId, Guid VenueId, Guid ResourceA, Guid ResourceB);

    private static async Task<Ids> SeedAsync(NpgsqlConnection connection, string slug = "t")
    {
        var tenantId = await connection.ExecuteScalarAsync<Guid>(
            "INSERT INTO tenants (name, slug, default_time_zone) VALUES ('T', @slug, 'UTC') RETURNING id",
            new { slug = slug + Guid.NewGuid().ToString("N")[..8] });
        var venueId = await connection.ExecuteScalarAsync<Guid>(
            "INSERT INTO venues (tenant_id, name, slug, time_zone) VALUES (@tenantId, 'V', 'v', 'UTC') RETURNING id", new { tenantId });
        var typeId = await connection.ExecuteScalarAsync<Guid>(
            "INSERT INTO resource_types (tenant_id, code, name) VALUES (@tenantId, 'TABLE', 'Table') RETURNING id", new { tenantId });
        const string resourceSql = """
            INSERT INTO bookable_resources (tenant_id, venue_id, resource_type_id, name, code, min_capacity, capacity)
            VALUES (@tenantId, @venueId, @typeId, @code, @code, 1, 4) RETURNING id
            """;
        var a = await connection.ExecuteScalarAsync<Guid>(resourceSql, new { tenantId, venueId, typeId, code = "A" });
        var b = await connection.ExecuteScalarAsync<Guid>(resourceSql, new { tenantId, venueId, typeId, code = "B" });
        return new Ids(tenantId, venueId, a, b);
    }

    private static Task<Guid> BlockAsync(NpgsqlConnection connection, Ids ids, DateTime start, DateTime end) =>
        connection.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO resource_blocks (tenant_id, venue_id, reason, starts_at, ends_at)
            VALUES (@TenantId, @VenueId, 'MAINTENANCE', @start, @end) RETURNING id
            """,
            new { ids.TenantId, ids.VenueId, start, end });

    private static async Task<Guid> AllocateAsync(NpgsqlConnection connection, Ids ids, Guid resourceId, DateTime start, DateTime end, bool active = true)
    {
        var blockId = await BlockAsync(connection, ids, start, end);
        return await connection.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO booking_allocations (tenant_id, venue_id, resource_id, allocation_type, block_id, starts_at, ends_at,
                                             is_active, released_at, release_reason)
            VALUES (@TenantId, @VenueId, @resourceId, 'BLOCK', @blockId, @start, @end, @active,
                    CASE WHEN @active THEN NULL ELSE now() END, CASE WHEN @active THEN NULL ELSE 'BLOCK_CANCELLED' END)
            RETURNING id
            """,
            new { ids.TenantId, ids.VenueId, resourceId, blockId, start, end, active });
    }

    private static async Task<string?> SqlStateOfAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (PostgresException ex)
        {
            return ex.SqlState;
        }
    }

    [Fact]
    public async Task Overlapping_active_allocations_on_one_resource_are_rejected()
    {
        await using var connection = new NpgsqlConnection(await fixture.CreateMigratedDatabaseAsync());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var ids = await SeedAsync(connection);
        await AllocateAsync(connection, ids, ids.ResourceA, Ten, Ten.AddHours(2));

        (await SqlStateOfAsync(() => AllocateAsync(connection, ids, ids.ResourceA, Ten.AddHours(1), Ten.AddHours(3))))
            .Should().Be(PostgresErrorCodes.ExclusionViolation);
        (await SqlStateOfAsync(() => AllocateAsync(connection, ids, ids.ResourceA, Ten.AddMinutes(30), Ten.AddMinutes(45))))
            .Should().Be(PostgresErrorCodes.ExclusionViolation, "a contained range overlaps too");
    }

    [Fact]
    public async Task Adjacent_inactive_and_other_resource_allocations_are_accepted()
    {
        await using var connection = new NpgsqlConnection(await fixture.CreateMigratedDatabaseAsync());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var ids = await SeedAsync(connection);
        await AllocateAsync(connection, ids, ids.ResourceA, Ten, Ten.AddHours(2));

        (await SqlStateOfAsync(() => AllocateAsync(connection, ids, ids.ResourceA, Ten.AddHours(2), Ten.AddHours(4))))
            .Should().BeNull("ranges are half-open, so [10,12) and [12,14) do not overlap");
        (await SqlStateOfAsync(() => AllocateAsync(connection, ids, ids.ResourceA, Ten.AddHours(-2), Ten)))
            .Should().BeNull();
        (await SqlStateOfAsync(() => AllocateAsync(connection, ids, ids.ResourceB, Ten, Ten.AddHours(2))))
            .Should().BeNull("a different resource");
        var inactiveId = await AllocateAsync(connection, ids, ids.ResourceA, Ten.AddHours(1), Ten.AddHours(3), active: false);

        (await SqlStateOfAsync(() => connection.ExecuteAsync(
                "UPDATE booking_allocations SET is_active = true, released_at = NULL, release_reason = NULL WHERE id = @inactiveId",
                new { inactiveId })))
            .Should().Be(PostgresErrorCodes.ExclusionViolation, "reactivating an overlapping allocation is also rejected");
    }

    [Fact]
    public async Task Allocation_integrity_checks_reject_inconsistent_rows()
    {
        await using var connection = new NpgsqlConnection(await fixture.CreateMigratedDatabaseAsync());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var ids = await SeedAsync(connection);
        var other = await SeedAsync(connection, "o");
        var blockId = await BlockAsync(connection, ids, Ten, Ten.AddHours(1));

        Task Insert(Guid tenantId, Guid venueId, Guid resourceId, string type, DateTime start, DateTime end, DateTime? expiresAt = null) =>
            connection.ExecuteAsync(
                """
                INSERT INTO booking_allocations (tenant_id, venue_id, resource_id, allocation_type, block_id, starts_at, ends_at, expires_at)
                VALUES (@tenantId, @venueId, @resourceId, @type, @blockId, @start, @end, @expiresAt)
                """,
                new { tenantId, venueId, resourceId, type, blockId, start, end, expiresAt });

        (await SqlStateOfAsync(() => Insert(ids.TenantId, ids.VenueId, ids.ResourceA, "BOOKING", Ten, Ten.AddHours(1))))
            .Should().Be(PostgresErrorCodes.CheckViolation, "a BOOKING allocation must reference a booking, not a block");
        (await SqlStateOfAsync(() => Insert(ids.TenantId, ids.VenueId, ids.ResourceA, "BLOCK", Ten, Ten)))
            .Should().Be(PostgresErrorCodes.CheckViolation, "empty ranges are rejected");
        (await SqlStateOfAsync(() => Insert(other.TenantId, other.VenueId, ids.ResourceA, "BLOCK", Ten, Ten.AddHours(1))))
            .Should().Be(PostgresErrorCodes.ForeignKeyViolation, "a resource of another tenant cannot be claimed");
        (await SqlStateOfAsync(() => connection.ExecuteAsync(
                """
                INSERT INTO resource_holds (tenant_id, venue_id, owner_type, owner_reference, token_hash, party_size, starts_at, ends_at, expires_at)
                VALUES (@TenantId, @VenueId, 'USER', 'x', '\x00'::bytea, 2, @start, @end, @end)
                """,
                new { ids.TenantId, ids.VenueId, start = Ten, end = Ten.AddHours(1) })))
            .Should().Be(PostgresErrorCodes.CheckViolation, "hold token hashes are 32-byte SHA-256 digests");
    }

    [Fact]
    public async Task Concurrent_transactions_cannot_both_claim_the_same_slot()
    {
        var connectionString = await fixture.CreateMigratedDatabaseAsync();
        await using var setup = new NpgsqlConnection(connectionString);
        await setup.OpenAsync(TestContext.Current.CancellationToken);
        var ids = await SeedAsync(setup);
        var blockA = await BlockAsync(setup, ids, Ten, Ten.AddHours(2));
        var blockB = await BlockAsync(setup, ids, Ten.AddHours(1), Ten.AddHours(3));

        await using var first = new NpgsqlConnection(connectionString);
        await using var second = new NpgsqlConnection(connectionString);
        await first.OpenAsync(TestContext.Current.CancellationToken);
        await second.OpenAsync(TestContext.Current.CancellationToken);
        await using var tx1 = await first.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using var tx2 = await second.BeginTransactionAsync(TestContext.Current.CancellationToken);

        const string sql = """
            INSERT INTO booking_allocations (tenant_id, venue_id, resource_id, allocation_type, block_id, starts_at, ends_at)
            VALUES (@TenantId, @VenueId, @ResourceA, 'BLOCK', @blockId, @start, @end)
            """;
        await first.ExecuteAsync(sql, new { ids.TenantId, ids.VenueId, ids.ResourceA, blockId = blockA, start = Ten, end = Ten.AddHours(2) }, tx1);

        // The second insert blocks on the first transaction's uncommitted row, then fails once it commits.
        var secondInsert = Task.Run(async () => await SqlStateOfAsync(() =>
            second.ExecuteAsync(sql, new { ids.TenantId, ids.VenueId, ids.ResourceA, blockId = blockB, start = Ten.AddHours(1), end = Ten.AddHours(3) }, tx2)));
        await Task.Delay(300, TestContext.Current.CancellationToken);
        secondInsert.IsCompleted.Should().BeFalse("the exclusion constraint waits for the conflicting transaction");
        await tx1.CommitAsync(TestContext.Current.CancellationToken);

        (await secondInsert).Should().Be(PostgresErrorCodes.ExclusionViolation);
        (await setup.ExecuteScalarAsync<long>("SELECT count(*) FROM booking_allocations WHERE resource_id = @ResourceA AND is_active", ids))
            .Should().Be(1);
    }
}
