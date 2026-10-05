using System.Collections.Frozen;
using System.Text.Json;
using BookingPlatform.Application.Common.Outbox;
using BookingPlatform.Application.Common.Time;
using BookingPlatform.Infrastructure.Outbox;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Npgsql;

namespace BookingPlatform.Api.Realtime;

public sealed class RealtimeOptions
{
    public const string SectionName = "Realtime";

    /// <summary>Runs the REALTIME outbox dispatcher in this Api instance (any number of instances may run it).</summary>
    public bool DispatcherEnabled { get; set; } = true;

    /// <summary>Redis channel prefix for the SignalR backplane; defaults to <c>bp-{environment}</c>.</summary>
    public string? ChannelPrefix { get; set; }

    public int BatchSize { get; set; } = 100;

    public int PollSeconds { get; set; } = 2;

    /// <summary>Older notifications are skipped: clients resynchronise on reconnect, so stale events add nothing.</summary>
    public int MaxAgeSeconds { get; set; } = 300;

    public bool IsValid() => BatchSize is >= 1 and <= 1000 && PollSeconds is >= 1 and <= 60 && MaxAgeSeconds is >= 10 and <= 86400;
}

/// <summary>Publishes committed REALTIME outbox rows to venue groups; the Redis backplane fans them out to every instance.</summary>
public sealed class RealtimeOutboxDispatcher(
    OutboxStore store,
    NpgsqlDataSource dataSource,
    IHubContext<VenueHub> hub,
    IOptions<RealtimeOptions> options,
    IClock clock,
    ILogger<RealtimeOutboxDispatcher> logger) : OutboxDispatcher(store, dataSource, clock, logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly FrozenSet<string> KnownEvents = new[]
    {
        RealtimeEvents.BookingCreated, RealtimeEvents.BookingUpdated, RealtimeEvents.BookingCancelled, RealtimeEvents.BookingMoved,
        RealtimeEvents.BookingStatusChanged, RealtimeEvents.ResourceUpdated, RealtimeEvents.ResourceBlocked, RealtimeEvents.HoldCreated,
        RealtimeEvents.HoldExpired, RealtimeEvents.FloorPlanUpdated,
    }.ToFrozenSet(StringComparer.Ordinal);

    protected override OutboxDispatcherSettings Settings { get; } = new(
        OutboxDestinations.Realtime,
        options.Value.BatchSize,
        TimeSpan.FromSeconds(options.Value.PollSeconds),
        Lease: TimeSpan.FromSeconds(30),
        MaxAttempts: 5,
        MaxAge: TimeSpan.FromSeconds(options.Value.MaxAgeSeconds));

    protected override async Task HandleAsync(ClaimedOutboxMessage message, CancellationToken cancellationToken)
    {
        if (!KnownEvents.Contains(message.Type))
        {
            throw new OutboxPermanentException($"Unknown realtime event type '{message.Type}'.");
        }

        if (message.TenantId is not { } tenantId)
        {
            throw new OutboxPermanentException("Realtime messages must belong to a tenant.");
        }

        var payload = JsonSerializer.Deserialize<RealtimePayload>(message.Payload, JsonOptions)
            ?? throw new OutboxPermanentException("The realtime payload is empty.");
        await hub.Clients.Group(RealtimeGroups.Venue(tenantId, payload.VenueId)).SendAsync(message.Type, payload, cancellationToken);
    }
}
