using BookingPlatform.Application.Common.Time;
using BookingPlatform.Infrastructure.Outbox;
using BookingPlatform.Infrastructure.Persistence;
using BookingPlatform.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace BookingPlatform.IntegrationTests.Outbox;

/// <summary>
/// TESTING.md §4 outbox behaviour against a real database: success, retry with backoff, dead-letter, permanent failures,
/// lease reclaim and stale skipping. Uses NOTIFICATION rows so the Api's REALTIME dispatcher never interferes.
/// </summary>
public sealed class OutboxDispatcherTests(InfrastructureFixture fixture) : ApiTestBase(fixture)
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        DapperConfiguration.Configure();
    }

    [Fact]
    public async Task Successful_messages_are_processed_once_and_failures_retry_then_dead_letter()
    {
        await using var dataSource = NpgsqlDataSource.Create(Data.ConnectionString);
        var dispatcher = new TestDispatcher(new OutboxStore(dataSource), dataSource, maxAttempts: 3);
        var ok = await InsertAsync("Ok");
        var failing = await InsertAsync("Fail");
        var malformed = await InsertAsync("Permanent");

        await dispatcher.DrainAsync(Ct);

        dispatcher.Handled.Should().BeEquivalentTo(["Ok", "Fail", "Permanent"]);
        (await StatusAsync(ok)).Should().Be(("PROCESSED", 1));
        (await StatusAsync(failing)).Should().Be(("FAILED", 1));
        (await StatusAsync(malformed)).Should().Be(("DEAD", 1), "permanent failures are not retried");
        (await Data.ScalarAsync<string>("SELECT last_error FROM outbox_messages WHERE id = @failing", new { failing }))
            .Should().StartWith("InvalidOperationException: boom");
        (await Data.ScalarAsync<bool>("SELECT available_at > now() + interval '1 second' FROM outbox_messages WHERE id = @failing", new { failing }))
            .Should().BeTrue("the retry is delayed with backoff");

        dispatcher.Handled.Clear();
        await dispatcher.DrainAsync(Ct);
        dispatcher.Handled.Should().BeEmpty("nothing is due yet");

        await Data.ExecuteAsync("UPDATE outbox_messages SET available_at = now() WHERE id = @failing", new { failing });
        await dispatcher.DrainAsync(Ct);
        (await StatusAsync(failing)).Should().Be(("FAILED", 2));
        await Data.ExecuteAsync("UPDATE outbox_messages SET available_at = now() WHERE id = @failing", new { failing });
        await dispatcher.DrainAsync(Ct);
        (await StatusAsync(failing)).Should().Be(("DEAD", 3), "dead-lettered after the attempt limit");
        (await StatusAsync(ok)).Should().Be(("PROCESSED", 1), "processed rows are never dispatched again");
    }

    [Fact]
    public async Task Expired_leases_are_reclaimed_and_stale_messages_skipped()
    {
        await using var dataSource = NpgsqlDataSource.Create(Data.ConnectionString);
        var dispatcher = new TestDispatcher(new OutboxStore(dataSource), dataSource, maxAttempts: 3, maxAge: TimeSpan.FromMinutes(5));
        var crashed = await InsertAsync("Ok");
        var leased = await InsertAsync("Ok");
        var stale = await InsertAsync("Ok");
        await Data.ExecuteAsync(
            "UPDATE outbox_messages SET status = 'PROCESSING', attempt_count = 1, locked_until = now() - interval '1 second', locked_by = 'dead-instance' WHERE id = @crashed",
            new { crashed });
        await Data.ExecuteAsync(
            "UPDATE outbox_messages SET status = 'PROCESSING', attempt_count = 1, locked_until = now() + interval '1 minute', locked_by = 'live-instance' WHERE id = @leased",
            new { leased });
        await Data.ExecuteAsync("UPDATE outbox_messages SET created_at = now() - interval '1 hour' WHERE id = @stale", new { stale });

        await dispatcher.DrainAsync(Ct);

        (await StatusAsync(crashed)).Should().Be(("PROCESSED", 2), "a lease that expired is reclaimed");
        (await StatusAsync(leased)).Should().Be(("PROCESSING", 1), "a live lease is left alone");
        (await StatusAsync(stale)).Should().Be(("PROCESSED", 1));
        (await Data.ScalarAsync<string>("SELECT last_error FROM outbox_messages WHERE id = @stale", new { stale })).Should().StartWith("Skipped");
        dispatcher.Handled.Should().HaveCount(1);
    }

    private async Task<Guid> InsertAsync(string type)
    {
        var tenantId = await Data.CreateTenantAsync();
        return await Data.ScalarAsync<Guid>(
            "INSERT INTO outbox_messages (tenant_id, destination, type, payload) VALUES (@tenantId, 'NOTIFICATION', @type, '{}'::jsonb) RETURNING id",
            new { tenantId, type });
    }

    private async Task<(string Status, int Attempts)> StatusAsync(Guid id)
    {
        var rows = await Data.QueryAsync<(string, int)>("SELECT status, attempt_count FROM outbox_messages WHERE id = @id", new { id });
        return rows.Single();
    }

    private sealed class TestDispatcher(OutboxStore store, NpgsqlDataSource dataSource, int maxAttempts, TimeSpan? maxAge = null)
        : OutboxDispatcher(store, dataSource, new SystemClock(), NullLogger.Instance)
    {
        public List<string> Handled { get; } = [];

        protected override OutboxDispatcherSettings Settings { get; } =
            new("NOTIFICATION", BatchSize: 10, PollInterval: TimeSpan.FromSeconds(1), Lease: TimeSpan.FromSeconds(30), maxAttempts, maxAge);

        protected override Task HandleAsync(ClaimedOutboxMessage message, CancellationToken cancellationToken)
        {
            Handled.Add(message.Type);
            return message.Type switch
            {
                "Fail" => throw new InvalidOperationException("boom"),
                "Permanent" => throw new OutboxPermanentException("malformed"),
                _ => Task.CompletedTask,
            };
        }
    }
}
