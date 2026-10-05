using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using BookingPlatform.Api.Realtime;
using BookingPlatform.IntegrationTests.Bookings;
using BookingPlatform.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using static BookingPlatform.IntegrationTests.Bookings.BookingScenario;

namespace BookingPlatform.IntegrationTests.Realtime;

/// <summary>REALTIME.md §7: venue hub authorisation, tenant isolation and outbox-driven delivery after commit.</summary>
public sealed class RealtimeTests(InfrastructureFixture fixture) : ApiTestBase(fixture)
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(15);

    private static readonly string[] Events =
    [
        "BookingCreated", "BookingUpdated", "BookingCancelled", "BookingMoved", "BookingStatusChanged", "ResourceUpdated",
        "ResourceBlocked", "HoldCreated", "HoldExpired", "FloorPlanUpdated",
    ];

    private readonly List<HubConnection> _connections = [];

    public override async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections)
        {
            await connection.DisposeAsync();
        }

        await base.DisposeAsync();
    }

    [Fact]
    public async Task Joined_staff_receive_booking_events_after_commit_without_personal_data()
    {
        var s = await BookingScenario.CreateAsync(Data, SignInAsync);
        await DrainedAsync();
        var (connection, events) = await ConnectAsync(s.Session);
        await connection.InvokeAsync("JoinVenue", s.VenueId, Ct);

        var booking = await s.CreateBookingOkAsync(s.Booking("19:00", 2, Resources(s.T1)));
        var id = booking.GetProperty("id").GetGuid();
        var created = await events.NextAsync();
        created.Type.Should().Be("BookingCreated");
        created.Payload.GetProperty("entityId").GetGuid().Should().Be(id);
        created.Payload.GetProperty("venueId").GetGuid().Should().Be(s.VenueId);
        created.Payload.GetProperty("version").GetInt32().Should().Be(1);
        created.Payload.GetProperty("businessDate").GetString().Should().Be(DateText);
        created.Payload.GetProperty("type").GetString().Should().Be("BookingCreated");
        created.Payload.EnumerateObject().Select(p => p.Name).Should()
            .BeEquivalentTo(["eventId", "type", "venueId", "entityId", "version", "businessDate", "occurredAt"], "payloads carry ids only");
        created.Payload.GetRawText().Should().NotContain("Ada").And.NotContain("example.test").And.NotContain("Window seat");

        var conflict = await s.CreateBookingAsync(s.Booking("19:30", 2, Resources(s.T1)));
        await conflict.ShouldBeProblemAsync(HttpStatusCode.Conflict, "RESOURCE_NOT_AVAILABLE");

        (await s.Session.SendAsync(HttpMethod.Put, $"/api/v1/bookings/{id}", new { partySize = 3, version = 1 })).StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await events.NextAsync();
        updated.Type.Should().Be("BookingUpdated", "the rolled-back conflicting booking emitted nothing");
        updated.Payload.GetProperty("version").GetInt32().Should().Be(2);

        (await s.Session.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{id}/move", new { time = "20:00", allocation = Resources(s.T2) }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await events.NextAsync()).Type.Should().Be("BookingMoved");

        (await s.Session.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{id}/cancel", new { reason = "Guest called" })).StatusCode.Should().Be(HttpStatusCode.OK);
        var cancelled = await events.NextAsync();
        cancelled.Type.Should().Be("BookingCancelled");
        cancelled.Payload.GetProperty("version").GetInt32().Should().Be(4);

        await connection.InvokeAsync("LeaveVenue", s.VenueId, Ct);
        await s.CreateBookingOkAsync(s.Booking("21:00", 2, Resources(s.T3)));
        await events.ExpectNoneAsync(TimeSpan.FromSeconds(3));

        await DrainedAsync();
        (await Data.ScalarAsync<long>(
                "SELECT count(*) FROM outbox_messages WHERE destination = 'REALTIME' AND status = 'PROCESSED' AND type LIKE 'Booking%' AND last_error IS NULL"))
            .Should().Be(5);
        (await Data.ScalarAsync<long>("SELECT count(*) FROM outbox_messages WHERE destination = 'NOTIFICATION' AND status = 'PENDING'"))
            .Should().BeGreaterThan(0, "the Api dispatches only REALTIME rows");
    }

    [Fact]
    public async Task Other_tenants_cannot_join_and_receive_nothing()
    {
        var s = await BookingScenario.CreateAsync(Data, SignInAsync);
        var (_, outsider, _) = await Data.CreateTenantWithUserAsync("TENANT_OWNER");
        var (connection, events) = await ConnectAsync(await SignInAsync(outsider));

        var join = () => connection.InvokeAsync("JoinVenue", s.VenueId, Ct);
        (await join.Should().ThrowAsync<HubException>()).Which.Message.Should().Contain(VenueHub.ForbiddenMessage);
        var unknown = () => connection.InvokeAsync("JoinVenue", Guid.NewGuid(), Ct);
        (await unknown.Should().ThrowAsync<HubException>()).Which.Message.Should().Contain(VenueHub.ForbiddenMessage, "unknown venues look the same");

        await s.CreateBookingOkAsync(s.Booking("19:00", 2, Resources(s.T1)));
        await events.ExpectNoneAsync(TimeSpan.FromSeconds(3));
        connection.State.Should().Be(HubConnectionState.Connected, "a denied join does not close the connection");
    }

    [Fact]
    public async Task Venue_scoped_staff_join_only_assigned_venues_and_need_booking_view()
    {
        var s = await BookingScenario.CreateAsync(Data, SignInAsync);
        await DrainedAsync();
        var host = await Data.CreateUserAsync();
        var membership = await Data.AddMembershipAsync(s.TenantId, host.UserId, "HOST");
        var (hostConnection, _) = await ConnectAsync(await SignInAsync(host));
        var denied = () => hostConnection.InvokeAsync("JoinVenue", s.VenueId, Ct);
        await denied.Should().ThrowAsync<HubException>();

        await Data.ExecuteAsync("INSERT INTO tenant_user_venues (tenant_id, tenant_user_id, venue_id) VALUES (@TenantId, @TenantUserId, @venueId)",
            new { membership.TenantId, membership.TenantUserId, venueId = s.VenueId });
        var (assignedConnection, events) = await ConnectAsync(await SignInAsync(host));
        await assignedConnection.InvokeAsync("JoinVenue", s.VenueId, Ct);
        await s.CreateBookingOkAsync(s.Booking("19:00", 2, Resources(s.T1)));
        (await events.NextAsync()).Type.Should().Be("BookingCreated");

        var resource = await s.Session.SendAsync(HttpMethod.Put, $"/api/v1/resources/{s.T1}",
            new { resourceTypeId = s.TypeId, name = "Table one", code = "T1", minCapacity = 1, capacity = 4, isActive = true, isBookableOnline = true, sortOrder = 1 });
        resource.StatusCode.Should().Be(HttpStatusCode.OK, await resource.Content.ReadAsStringAsync(Ct));
        var resourceEvent = await events.NextAsync();
        resourceEvent.Type.Should().Be("ResourceUpdated");
        resourceEvent.Payload.GetProperty("entityId").GetGuid().Should().Be(s.T1);
    }

    [Fact]
    public async Task Connections_require_a_valid_token()
    {
        var anonymous = BuildConnection(() => null);
        var start = () => anonymous.StartAsync(Ct);
        await start.Should().ThrowAsync<Exception>("the hub requires authentication");

        var forged = BuildConnection(() => "not-a-jwt");
        var startForged = () => forged.StartAsync(Ct);
        await startForged.Should().ThrowAsync<Exception>();
    }

    private async Task<(HubConnection Connection, EventRecorder Events)> ConnectAsync(ApiSession session)
    {
        var token = session.AccessToken ?? throw new InvalidOperationException("The session is not signed in.");
        var connection = BuildConnection(() => token);
        var recorder = new EventRecorder();
        foreach (var name in Events)
        {
            connection.On<JsonElement>(name, payload => recorder.Record(name, payload));
        }

        await connection.StartAsync(Ct);
        return (connection, recorder);
    }

    /// <summary>Connects like the browser: WebSockets only, no negotiation, token in the <c>access_token</c> query string.</summary>
    private HubConnection BuildConnection(Func<string?> token)
    {
        var server = Factory.Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, VenueHub.Path.TrimStart('/')), options =>
            {
                options.SkipNegotiation = true;
                options.Transports = HttpTransportType.WebSockets;
                options.WebSocketFactory = async (context, cancellationToken) =>
                {
                    var uri = context.Uri.ToString();
                    var value = token();
                    if (value is not null)
                    {
                        uri += (uri.Contains('?', StringComparison.Ordinal) ? "&" : "?") + "access_token=" + Uri.EscapeDataString(value);
                    }

                    return await server.CreateWebSocketClient().ConnectAsync(new Uri(uri), cancellationToken);
                };
            })
            .Build();
        _connections.Add(connection);
        return connection;
    }

    /// <summary>Waits until every REALTIME row is dispatched, so setup events cannot arrive after a test joins.</summary>
    private Task DrainedAsync() => WaitUntilAsync(async () => await Data.ScalarAsync<long>(
        "SELECT count(*) FROM outbox_messages WHERE destination = 'REALTIME' AND status <> 'PROCESSED'") == 0);

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + EventTimeout;
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition was not met in time.");
            }

            await Task.Delay(100, Ct);
        }
    }

    private sealed class EventRecorder
    {
        private readonly Channel<(string Type, JsonElement Payload)> _events = Channel.CreateUnbounded<(string, JsonElement)>();

        public void Record(string type, JsonElement payload) => _events.Writer.TryWrite((type, payload.Clone()));

        public async Task<(string Type, JsonElement Payload)> NextAsync()
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            timeout.CancelAfter(EventTimeout);
            return await _events.Reader.ReadAsync(timeout.Token);
        }

        public async Task ExpectNoneAsync(TimeSpan wait)
        {
            await Task.Delay(wait, Ct);
            _events.Reader.TryRead(out var unexpected).Should().BeFalse($"no event was expected but {unexpected.Type} arrived");
        }
    }
}
