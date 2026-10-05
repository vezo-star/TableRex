using BookingPlatform.Infrastructure.Housekeeping;
using BookingPlatform.Infrastructure.Persistence;
using BookingPlatform.IntegrationTests.Infrastructure;
using Npgsql;

namespace BookingPlatform.IntegrationTests.Bookings;

public sealed class HousekeepingTests(InfrastructureFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Sweeper_expires_holds_writes_outbox_rows_and_purge_removes_expired_keys()
    {
        var s = await BookingScenario.CreateAsync(Data, SignInAsync);
        var live = await Data.SeedHoldAsync(s.TenantId, s.VenueId, s.T1, BookingScenario.Local(19), BookingScenario.Local(21), DateTime.UtcNow.AddMinutes(5));
        var expired = await Data.SeedHoldAsync(s.TenantId, s.VenueId, s.T2, BookingScenario.Local(19), BookingScenario.Local(21), DateTime.UtcNow.AddMinutes(-1));
        await Data.ExecuteAsync(
            """
            INSERT INTO idempotency_keys (tenant_id, scope, key, request_hash, created_at, expires_at)
            VALUES (@tenantId, 'booking.create:user:x', 'old', sha256('a'::bytea), now() - interval '2 days', now() - interval '1 day'),
                   (@tenantId, 'booking.create:user:x', 'new', sha256('b'::bytea), now(), now() + interval '1 day')
            """,
            new { tenantId = s.TenantId });

        await using var dataSource = NpgsqlDataSource.Create(Data.ConnectionString);
        await using var session = new DbSession(dataSource);
        var repository = new HousekeepingRepository(session);

        (await repository.SweepExpiredHoldsAsync(DateTime.UtcNow, 100, Ct)).Should().Be(1);
        (await Data.ScalarAsync<string>("SELECT status FROM resource_holds WHERE id = @expired", new { expired })).Should().Be("EXPIRED");
        (await Data.ScalarAsync<string>("SELECT status FROM resource_holds WHERE id = @live", new { live })).Should().Be("ACTIVE");
        (await Data.ScalarAsync<long>("SELECT count(*) FROM booking_allocations WHERE hold_id = @expired AND NOT is_active AND release_reason = 'HOLD_EXPIRED'", new { expired }))
            .Should().Be(1);
        (await Data.ScalarAsync<long>("SELECT count(*) FROM outbox_messages WHERE type = 'HoldExpired' AND payload->>'entityId' = @id AND payload->>'eventId' = id::text",
            new { id = expired.ToString() })).Should().Be(1);
        (await repository.SweepExpiredHoldsAsync(DateTime.UtcNow, 100, Ct)).Should().Be(0, "sweeping is idempotent");

        var purged = await repository.PurgeAsync(DateTime.UtcNow, 100, Ct);
        purged.IdempotencyKeys.Should().Be(1);
        (await Data.ScalarAsync<long>("SELECT count(*) FROM idempotency_keys")).Should().Be(1);
    }
}
