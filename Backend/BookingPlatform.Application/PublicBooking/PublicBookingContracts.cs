namespace BookingPlatform.Application.PublicBooking;

/// <summary>What a guest sees about a venue. No internal ids, resources, staff or customer data.</summary>
public sealed record PublicVenueDto(
    string TenantSlug,
    string TenantName,
    string Slug,
    string Name,
    string? Description,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? Region,
    string? PostalCode,
    string? CountryCode,
    string? Phone,
    string? Email,
    string TimeZone,
    int MinPartySize,
    int MaxPartySize,
    int DurationMinutes,
    int MinLeadTimeMinutes,
    DateOnly Today,
    DateOnly LastBookableDate,
    int CancellationCutoffMinutes,
    bool AutoConfirm);

public sealed record PublicTimeSlotDto(TimeOnly Time, DateTime StartsAt);

/// <summary>Bookable start times only; <c>IsClosed</c> distinguishes "closed that day" from "fully booked".</summary>
public sealed record PublicAvailabilityDto(DateOnly Date, string TimeZone, int PartySize, int DurationMinutes, bool IsClosed, IReadOnlyList<PublicTimeSlotDto> Times);

/// <param name="Website">Honeypot: hidden in the booking form, so a value means an automated submission.</param>
public sealed record PublicBookingRequest(
    DateOnly Date,
    TimeOnly Time,
    int PartySize,
    string FirstName,
    string? LastName,
    string Email,
    string? Phone,
    string? SpecialRequests,
    bool MarketingConsent = false,
    string? Website = null);

/// <summary>Returned when a booking is created (and on an idempotent replay). <c>ManageToken</c> opens the guest's booking page.</summary>
public sealed record PublicBookingConfirmationDto(
    string Reference,
    string Status,
    string VenueName,
    DateOnly Date,
    TimeOnly Time,
    int DurationMinutes,
    int PartySize,
    string TimeZone,
    DateTime StartsAt,
    string? ManageToken);

/// <summary>The guest's view of their booking, opened with the manage token.</summary>
public sealed record PublicBookingDto(
    string Reference,
    string Status,
    string TenantSlug,
    string VenueSlug,
    string VenueName,
    string? VenuePhone,
    string? VenueEmail,
    DateOnly Date,
    TimeOnly Time,
    int DurationMinutes,
    int PartySize,
    string TimeZone,
    DateTime StartsAt,
    string GuestFirstName,
    string? SpecialRequests,
    bool CanCancel,
    DateTime? CancelUntil,
    DateTime? CancelledAt);

/// <summary>The tenant and venue behind a pair of public slugs (or ids).</summary>
public sealed record PublicVenueLocator(Guid TenantId, string TenantSlug, string TenantName, string TenantStatus, Guid VenueId, string VenueSlug);

public interface IPublicDirectory
{
    Task<PublicVenueLocator?> FindAsync(string tenantSlug, string venueSlug, CancellationToken cancellationToken);

    Task<PublicVenueLocator?> GetAsync(Guid tenantId, Guid venueId, CancellationToken cancellationToken);
}

/// <summary>
/// Guest manage-booking tokens: an HMAC of the booking id under a dedicated server key, so the token can be re-derived
/// for idempotent replays and notification links without ever being stored. Only its SHA-256 is persisted.
/// </summary>
public interface IPublicBookingTokens
{
    string Create(Guid bookingId);

    byte[] Hash(string token);
}

public sealed class PublicBookingOptions
{
    public const string SectionName = "PublicBooking";
    public const int MinimumKeyBytes = 32;

    /// <summary>Secret for manage-booking tokens (≥32 bytes). Rotating it invalidates links in emails already sent.</summary>
    public string TokenKey { get; set; } = string.Empty;

    /// <summary>Upcoming PENDING/CONFIRMED bookings one contact may hold at a venue.</summary>
    public int MaxActiveBookingsPerContact { get; set; } = 3;

    public static bool IsValid(PublicBookingOptions options) =>
        options is not null
        && System.Text.Encoding.UTF8.GetByteCount(options.TokenKey ?? string.Empty) >= MinimumKeyBytes
        && !options.TokenKey!.Contains("change-me", StringComparison.OrdinalIgnoreCase)
        && options.MaxActiveBookingsPerContact is >= 1 and <= 100;
}
