using System.Net;
using System.Text.Json;
using BookingPlatform.IntegrationTests.Infrastructure;

namespace BookingPlatform.IntegrationTests.Bookings;

/// <summary>
/// A venue in Africa/Johannesburg (UTC+2, no DST) open 17:00-23:00 every day, tables T1-T3 (capacity 1-4) and the
/// combination "T2 + T3" (5-8 guests). <see cref="Date"/> is 10 days ahead so every slot is in the future.
/// </summary>
public sealed record BookingScenario(Guid TenantId, ApiSession Session, Guid VenueId, Guid TypeId, Guid T1, Guid T2, Guid T3, Guid CombinationId)
{
    public static readonly DateOnly Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));

    public static string DateText => Date.ToString("yyyy-MM-dd");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>UTC instant for a Johannesburg local time on <see cref="Date"/>.</summary>
    public static DateTime Local(int hour, int minute = 0) => Date.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Utc).AddHours(-2);

    public static async Task<BookingScenario> CreateAsync(TestData data, Func<TestUser, Task<ApiSession>> signIn)
    {
        var (tenantId, owner, _) = await data.CreateTenantWithUserAsync("TENANT_OWNER");
        var session = await signIn(owner);
        var venueId = await PostIdAsync(session, "/api/v1/venues",
            new { name = "Bistro", slug = "bistro-" + Guid.NewGuid().ToString("N")[..8], timeZone = "Africa/Johannesburg" });
        var periods = Enumerable.Range(1, 7).Select(day => new { dayOfWeek = day, openTime = "17:00", closeTime = "23:00" });
        var hours = await session.SendAsync(HttpMethod.Put, $"/api/v1/venues/{venueId}/operating-hours", new { periods });
        hours.StatusCode.Should().Be(HttpStatusCode.OK, await hours.Content.ReadAsStringAsync(Ct));

        var typeId = await PostIdAsync(session, "/api/v1/resource-types", new { code = "TABLE", name = "Table" });
        var t1 = await CreateTableAsync(session, venueId, typeId, "T1", 1);
        var t2 = await CreateTableAsync(session, venueId, typeId, "T2", 2);
        var t3 = await CreateTableAsync(session, venueId, typeId, "T3", 3);
        var combinationId = await PostIdAsync(session, "/api/v1/resource-combinations", new
        {
            venueId,
            name = "T2 + T3",
            minCapacity = 5,
            capacity = 8,
            members = new object[] { new { resourceId = t2 }, new { resourceId = t3 } },
        });
        return new BookingScenario(tenantId, session, venueId, typeId, t1, t2, t3, combinationId);
    }

    public static Task<Guid> CreateTableAsync(ApiSession session, Guid venueId, Guid typeId, string code, int sortOrder) =>
        PostIdAsync(session, "/api/v1/resources",
            new { venueId, resourceTypeId = typeId, name = code, code, minCapacity = 1, capacity = 4, sortOrder });

    public static async Task<Guid> PostIdAsync(ApiSession session, string path, object body)
    {
        var response = await session.SendAsync(HttpMethod.Post, path, body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    /// <summary>A create-booking body for <see cref="Date"/>; <paramref name="allocation"/> defaults to AUTO.</summary>
    public object Booking(string time, int partySize = 2, object? allocation = null, int? durationMinutes = 120, string? holdToken = null,
        object? customer = null, string? status = null, string? source = null) => new
    {
        venueId = VenueId,
        customer = customer ?? new { firstName = "Ada", lastName = "Lovelace", email = $"ada-{Guid.NewGuid():N}@example.test", phone = "+27 82 555 0101" },
        date = DateText,
        time,
        durationMinutes,
        partySize,
        allocation = allocation ?? new { mode = "AUTO" },
        holdToken,
        source,
        status,
        specialRequests = "Window seat",
        internalNotes = "Regular",
    };

    public static object Resources(params Guid[] ids) => new { mode = "RESOURCES", resourceIds = ids };

    public object Combination() => new { mode = "COMBINATION", combinationId = CombinationId };

    public Task<HttpResponseMessage> CreateBookingAsync(object body, string? key = null) => Session.PostIdempotentAsync("/api/v1/bookings", body, key);

    public async Task<JsonElement> CreateBookingOkAsync(object body)
    {
        var response = await CreateBookingAsync(body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await response.JsonAsync();
    }

    public static IReadOnlyList<Guid> ResourceIdsOf(JsonElement booking) =>
        booking.GetProperty("resources").EnumerateArray().Select(r => r.GetProperty("resourceId").GetGuid()).ToList();
}

public static class BookingInvariants
{
    /// <summary>The TESTING.md §5 invariants: no overlapping active allocations, none for terminal bookings, every active booking allocated.</summary>
    public static async Task AssertAsync(TestData data)
    {
        (await data.ScalarAsync<long>(
                """
                SELECT count(*) FROM booking_allocations a JOIN booking_allocations b
                  ON a.resource_id = b.resource_id AND a.id < b.id AND a.is_active AND b.is_active AND a.period && b.period
                """))
            .Should().Be(0, "active allocations never overlap");
        (await data.ScalarAsync<long>(
                """
                SELECT count(*) FROM booking_allocations al JOIN bookings bk ON bk.id = al.booking_id
                WHERE al.is_active AND bk.status IN ('CANCELLED', 'NO_SHOW')
                """))
            .Should().Be(0, "terminal bookings hold no active allocation");
        (await data.ScalarAsync<long>(
                """
                SELECT count(*) FROM bookings bk
                WHERE bk.status IN ('PENDING', 'HELD', 'CONFIRMED', 'CHECKED_IN', 'SEATED')
                  AND (NOT EXISTS (SELECT 1 FROM booking_allocations al WHERE al.booking_id = bk.id AND al.is_active)
                       OR NOT EXISTS (SELECT 1 FROM booking_resources br WHERE br.booking_id = bk.id))
                """))
            .Should().Be(0, "every active booking has resources and active allocations");
    }
}
