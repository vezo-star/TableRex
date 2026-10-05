using NodaTime;

namespace BookingPlatform.Domain.Time;

/// <summary>IANA time zones from the TZDB bundled with NodaTime (independent of the host OS).</summary>
public static class TimeZones
{
    public static bool IsValid(string? zoneId) =>
        !string.IsNullOrWhiteSpace(zoneId) && DateTimeZoneProviders.Tzdb.GetZoneOrNull(zoneId) is not null;

    public static DateTimeZone Get(string zoneId) =>
        DateTimeZoneProviders.Tzdb.GetZoneOrNull(zoneId)
        ?? throw new ArgumentException($"Unknown time zone '{zoneId}'.", nameof(zoneId));
}
