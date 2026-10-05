using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BookingPlatform.Application.Availability;
using BookingPlatform.Application.Bookings;
using BookingPlatform.Application.Common.Errors;
using BookingPlatform.Application.Common.Idempotency;
using BookingPlatform.Application.Common.RateLimiting;
using BookingPlatform.Application.Common.Validation;
using BookingPlatform.Application.Venues;
using BookingPlatform.Domain.Bookings;
using BookingPlatform.Domain.Identity;
using BookingPlatform.Domain.Time;
using BookingPlatform.Domain.Venues;
using FluentValidation;
using Microsoft.Extensions.Options;
using NodaTime;
using IClock = BookingPlatform.Application.Common.Time.IClock;

namespace BookingPlatform.Application.PublicBooking;

public sealed record PublicBookingResult(PublicBookingConfirmationDto Booking, bool Replayed);

/// <summary>
/// Anonymous booking for guests. The tenant and venue come from slugs, never from the client; bookings go through the
/// same engine as staff bookings (<see cref="BookingService.CreateCoreAsync{TResult}"/>), so the exclusion constraint
/// and idempotency protect public requests exactly like staff ones.
/// </summary>
public sealed partial class PublicBookingService(
    IPublicDirectory directory,
    IVenueRepository venues,
    VenueService venueService,
    AvailabilityService availability,
    BookingService bookingService,
    IBookingRepository bookings,
    IPublicBookingTokens tokens,
    IRateLimiter rateLimiter,
    IOptions<PublicBookingOptions> options,
    IClock clock,
    IValidator<PublicBookingRequest> validator)
{
    private const string VenueNotFoundMessage = "The venue was not found or does not take online bookings.";
    private const string BookingNotFoundMessage = "The booking was not found.";
    private const string GuestCancellationReason = "Cancelled by the guest online.";

    private static readonly string[] CancellableStatuses = [BookingStatus.Pending, BookingStatus.Held, BookingStatus.Confirmed];

    public async Task<PublicVenueDto> GetVenueAsync(string tenantSlug, string venueSlug, CancellationToken cancellationToken)
    {
        var (locator, venue) = await ResolveAsync(tenantSlug, venueSlug, cancellationToken);
        var rules = venue.Rules;
        var today = Today(venue);
        return new PublicVenueDto(locator.TenantSlug, locator.TenantName, venue.Slug, venue.Name, venue.Description, venue.AddressLine1,
            venue.AddressLine2, venue.City, venue.Region, venue.PostalCode, venue.CountryCode, venue.Phone, venue.Email, venue.TimeZone,
            rules.MinPartySize, rules.MaxPartySize, rules.DefaultDurationMinutes, rules.MinLeadTimeMinutes, today,
            today.AddDays(rules.MaxAdvanceDays), rules.PublicCancellationCutoffMinutes, rules.AutoConfirmPublicBookings);
    }

    public async Task<PublicAvailabilityDto> GetAvailabilityAsync(string tenantSlug, string venueSlug, DateOnly date, int partySize, CancellationToken cancellationToken)
    {
        var (locator, venue) = await ResolveAsync(tenantSlug, venueSlug, cancellationToken);
        EnsurePartySize(venue, partySize);
        EnsureWithinWindow(venue, date, "date");

        var schedule = await venueService.LoadScheduleAsync(locator.TenantId, ToRef(venue), date, date, cancellationToken);
        var businessDate = LocalDate.FromDateOnly(date);
        var duration = venue.Rules.DefaultDurationMinutes;
        if (schedule.GetPeriods(businessDate).Count == 0)
        {
            return new PublicAvailabilityDto(date, venue.TimeZone, partySize, duration, IsClosed: true, []);
        }

        var slots = await availability.CalculateOnlineAsync(locator.TenantId, venue, schedule, businessDate, partySize, EarliestStart(venue), cancellationToken);
        var times = slots.Where(s => s.IsAvailable)
            .Select(s => new PublicTimeSlotDto(s.LocalTime.ToTimeOnly(), s.Start.ToDateTimeUtc()))
            .ToList();
        return new PublicAvailabilityDto(date, venue.TimeZone, partySize, duration, IsClosed: false, times);
    }

    public async Task<PublicBookingResult> CreateAsync(
        string tenantSlug, string venueSlug, PublicBookingRequest request, IdempotencyRequest idempotencyRequest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await validator.EnsureValidAsync(request, cancellationToken);
        var (locator, venue) = await ResolveAsync(tenantSlug, venueSlug, cancellationToken);
        var tenantId = locator.TenantId;

        var contactKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Email.Trim().ToLowerInvariant())));
        var limit = await rateLimiter.AcquireAsync($"public-booking:contact:{contactKey}", 5, TimeSpan.FromHours(1), cancellationToken);
        if (!limit.Allowed)
        {
            throw new RateLimitedAppException(limit.RetryAfterSeconds);
        }

        EnsurePartySize(venue, request.PartySize);
        EnsureWithinWindow(venue, request.Date, "date");
        var schedule = await venueService.LoadScheduleAsync(tenantId, ToRef(venue), request.Date, request.Date, cancellationToken);
        var timing = BookingAllocator.ResolveTiming(schedule, venue.Rules, request.Date, request.Time, null);
        if (timing.Start < EarliestStart(venue))
        {
            throw new BusinessRuleAppException(ErrorCodes.BookingWindow,
                $"Online bookings must be made at least {venue.Rules.MinLeadTimeMinutes} minutes in advance.");
        }

        var period = schedule.GetPeriods(timing.BusinessDate).First(p => p.Contains(timing.Start, timing.End));
        if ((timing.Start - period.Start).TotalMinutes % venue.Rules.SlotIntervalMinutes != 0)
        {
            throw new ValidationAppException("time", "Choose one of the offered times.");
        }

        var bookingId = Guid.CreateVersion7();
        var token = tokens.Create(bookingId);
        var status = venue.Rules.AutoConfirmPublicBookings ? BookingStatus.Confirmed : BookingStatus.Pending;
        var engineRequest = new CreateBookingRequest(venue.Id,
            new BookingCustomerInput(null, request.FirstName.Trim(), Clean(request.LastName), request.Email.Trim(), Clean(request.Phone)),
            request.Date, request.Time, null, request.PartySize, Source: BookingSource.Public, Status: status, SpecialRequests: request.SpecialRequests);
        var creation = new BookingCreation(tenantId, venue, timing, engineRequest, BookingSource.Public, status, HistorySources.Public, null,
            $"public.booking.create:venue:{venue.Id}")
        {
            BookingId = bookingId,
            PublicTokenHash = tokens.Hash(token),
            OnlineOnly = true,
            MarketingConsent = request.MarketingConsent,
            MaxActiveBookingsPerCustomer = options.Value.MaxActiveBookingsPerContact,
        };

        var result = await bookingService.CreateCoreAsync(creation, idempotencyRequest,
            record => ToConfirmation(record, venue, token),
            confirmation => confirmation with { ManageToken = null },
            cancellationToken);
        if (result.Replay is { ResourceId: { } replayedId })
        {
            var original = await bookings.GetAsync(tenantId, replayedId, cancellationToken)
                ?? throw new NotFoundAppException(ErrorCodes.BookingNotFound, BookingNotFoundMessage);
            return new PublicBookingResult(ToConfirmation(original, venue, tokens.Create(replayedId)), Replayed: true);
        }

        return new PublicBookingResult(result.Value!, Replayed: false);
    }

    public async Task<PublicBookingDto> GetBookingAsync(string? token, CancellationToken cancellationToken)
    {
        var (record, locator, venue) = await FindByTokenAsync(token, cancellationToken);
        return ToPublic(record, locator, venue);
    }

    /// <summary>Guest cancellation within the venue's cutoff. Cancelling an already cancelled booking returns it unchanged.</summary>
    public async Task<PublicBookingDto> CancelAsync(string? token, CancellationToken cancellationToken)
    {
        var (record, locator, venue) = await FindByTokenAsync(token, cancellationToken);
        if (record.Status == BookingStatus.Cancelled)
        {
            return ToPublic(record, locator, venue);
        }

        var after = await bookingService.TransitionCoreAsync(locator.TenantId, record.Id, BookingCommand.Cancel, GuestCancellationReason, null, null,
            HistorySources.Public, locked => EnsureGuestCanCancel(locked, venue), cancellationToken);
        return ToPublic(after, locator, venue);
    }

    private void EnsureGuestCanCancel(BookingRecord booking, VenueDto venue)
    {
        if (!CancellableStatuses.Contains(booking.Status))
        {
            throw new BusinessRuleAppException(ErrorCodes.CancellationNotAllowed, "This booking can no longer be cancelled online. Contact the venue.");
        }

        if (clock.UtcNow >= CancelUntil(booking, venue))
        {
            throw new BusinessRuleAppException(ErrorCodes.CancellationNotAllowed,
                "The online cancellation window has closed for this booking. Contact the venue.");
        }
    }

    private async Task<(PublicVenueLocator Locator, VenueDto Venue)> ResolveAsync(string tenantSlug, string venueSlug, CancellationToken cancellationToken)
    {
        if (!IsSlug(tenantSlug) || !IsSlug(venueSlug))
        {
            throw new NotFoundAppException(ErrorCodes.VenueNotFound, VenueNotFoundMessage);
        }

        var locator = await directory.FindAsync(tenantSlug, venueSlug, cancellationToken);
        if (locator is null || locator.TenantStatus != TenantStatus.Active)
        {
            throw new NotFoundAppException(ErrorCodes.VenueNotFound, VenueNotFoundMessage);
        }

        var venue = await venues.GetAsync(locator.TenantId, locator.VenueId, cancellationToken);
        if (venue is null || venue.Status != VenueStatus.Active || !venue.Rules.PublicBookingEnabled)
        {
            throw new NotFoundAppException(ErrorCodes.VenueNotFound, VenueNotFoundMessage);
        }

        return (locator, venue);
    }

    private async Task<(BookingRecord Record, PublicVenueLocator Locator, VenueDto Venue)> FindByTokenAsync(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100)
        {
            throw new NotFoundAppException(ErrorCodes.BookingNotFound, BookingNotFoundMessage);
        }

        var found = await bookings.FindByPublicTokenAsync(tokens.Hash(token.Trim()), cancellationToken)
            ?? throw new NotFoundAppException(ErrorCodes.BookingNotFound, BookingNotFoundMessage);
        var record = await bookings.GetAsync(found.TenantId, found.BookingId, cancellationToken);
        var locator = record is null ? null : await directory.GetAsync(found.TenantId, record.VenueId, cancellationToken);
        var venue = locator is null ? null : await venues.GetAsync(found.TenantId, locator.VenueId, cancellationToken);
        if (record is null || locator is null || venue is null)
        {
            throw new NotFoundAppException(ErrorCodes.BookingNotFound, BookingNotFoundMessage);
        }

        return (record, locator, venue);
    }

    private PublicBookingDto ToPublic(BookingRecord r, PublicVenueLocator locator, VenueDto venue)
    {
        var cancelUntil = CancelUntil(r, venue);
        var canCancel = CancellableStatuses.Contains(r.Status) && clock.UtcNow < cancelUntil;
        return new PublicBookingDto(r.Reference, r.Status, locator.TenantSlug, locator.VenueSlug, venue.Name, venue.Phone, venue.Email,
            r.BusinessDate, LocalTimeOf(r.StartsAt, venue.TimeZone), (int)(r.EndsAt - r.StartsAt).TotalMinutes, r.PartySize, venue.TimeZone,
            r.StartsAt, r.Customer?.FirstName ?? "Guest", r.SpecialRequests, canCancel, canCancel ? cancelUntil : null, r.CancelledAt);
    }

    private static PublicBookingConfirmationDto ToConfirmation(BookingRecord r, VenueDto venue, string token) =>
        new(r.Reference, r.Status, venue.Name, r.BusinessDate, LocalTimeOf(r.StartsAt, venue.TimeZone), (int)(r.EndsAt - r.StartsAt).TotalMinutes,
            r.PartySize, venue.TimeZone, r.StartsAt, token);

    private static DateTime CancelUntil(BookingRecord booking, VenueDto venue) =>
        booking.StartsAt.AddMinutes(-venue.Rules.PublicCancellationCutoffMinutes);

    private static void EnsurePartySize(VenueDto venue, int partySize)
    {
        if (partySize < venue.Rules.MinPartySize || partySize > venue.Rules.MaxPartySize)
        {
            throw new ValidationAppException("partySize",
                $"Online bookings are for {venue.Rules.MinPartySize} to {venue.Rules.MaxPartySize} guests. Contact the venue for larger groups.");
        }
    }

    private void EnsureWithinWindow(VenueDto venue, DateOnly date, string field)
    {
        var today = Today(venue);
        var last = today.AddDays(venue.Rules.MaxAdvanceDays);
        if (date < today || date > last)
        {
            throw new ValidationAppException(field, $"Choose a date between {today:yyyy-MM-dd} and {last:yyyy-MM-dd}.");
        }
    }

    private DateOnly Today(VenueDto venue) =>
        Instant.FromDateTimeUtc(DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc)).InZone(TimeZones.Get(venue.TimeZone)).Date.ToDateOnly();

    private Instant EarliestStart(VenueDto venue) =>
        Instant.FromDateTimeUtc(DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc)) + Duration.FromMinutes(venue.Rules.MinLeadTimeMinutes);

    private static VenueRef ToRef(VenueDto venue) => new(venue.Id, venue.TimeZone, venue.Status);

    private static TimeOnly LocalTimeOf(DateTime utc, string timeZone) =>
        Instant.FromDateTimeUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).InZone(TimeZones.Get(timeZone)).TimeOfDay.ToTimeOnly();

    private static bool IsSlug(string? value) => value is { Length: > 0 and <= 63 } && SlugPattern().IsMatch(value);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}

public sealed class PublicBookingRequestValidator : AbstractValidator<PublicBookingRequest>
{
    public PublicBookingRequestValidator()
    {
        RuleFor(r => r.Date).NotEmpty();
        RuleFor(r => r.PartySize).InclusiveBetween(1, 1000);
        RuleFor(r => r.FirstName).Must(n => !string.IsNullOrWhiteSpace(n) && n.Trim().Length <= 100)
            .WithMessage("Enter your first name (up to 100 characters).");
        RuleFor(r => r.LastName).MaximumLength(100);
        RuleFor(r => r.Email).NotEmpty().WithMessage("Enter your email address.")
            .MaximumLength(254).EmailAddress().WithMessage("Enter a valid email address.");
        RuleFor(r => r.Phone).MaximumLength(40);
        RuleFor(r => r.Phone)
            .Must(p => BookingService.NormalizePhone(p) is not null)
            .When(r => !string.IsNullOrWhiteSpace(r.Phone))
            .WithMessage("Enter a phone number with 4-20 digits.");
        RuleFor(r => r.SpecialRequests).MaximumLength(1000);
        RuleFor(r => r.Website).Empty().WithMessage("This field must be left empty.");
    }
}
