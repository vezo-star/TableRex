using BookingPlatform.Application.Common.Errors;
using BookingPlatform.Application.Common.Security;
using BookingPlatform.Application.Common.Time;
using BookingPlatform.Application.Common.Validation;
using BookingPlatform.Application.Venues;
using BookingPlatform.Domain.Availability;
using BookingPlatform.Domain.Venues;
using FluentValidation;
using NodaTime;
using IClock = BookingPlatform.Application.Common.Time.IClock;

namespace BookingPlatform.Application.Availability;

public sealed class AvailabilityService(
    IAvailabilityRepository availability,
    IVenueRepository venues,
    VenueService venueService,
    IVenueAccessGuard guard,
    ITenantContext tenantContext,
    IClock clock,
    IValidator<AvailabilityRequest> validator)
{
    /// <summary>When a preferred time is given, slots within this many minutes either side are returned.</summary>
    public const int TimeWindowMinutes = 90;

    /// <summary>Staff search for a business date: every slot with ranked resource/combination options.</summary>
    public async Task<AvailabilityResponse> SearchAsync(AvailabilityRequest request, CancellationToken cancellationToken)
    {
        await validator.EnsureValidAsync(request, cancellationToken);
        var venueRef = await guard.EnsureAccessAsync(request.VenueId, cancellationToken);
        var tenantId = tenantContext.TenantId;
        var venue = (await venues.GetAsync(tenantId, request.VenueId, cancellationToken))
            ?? throw new NotFoundAppException(ErrorCodes.VenueNotFound, "The venue was not found.");
        var rules = venue.Rules;

        var durationMinutes = request.DurationMinutes ?? rules.DefaultDurationMinutes;
        if (durationMinutes < rules.MinDurationMinutes || durationMinutes > rules.MaxDurationMinutes)
        {
            throw new ValidationAppException("durationMinutes",
                $"Duration must be between {rules.MinDurationMinutes} and {rules.MaxDurationMinutes} minutes for this venue.");
        }

        if (request.SectionId is { } sectionId && await venues.GetSectionAsync(tenantId, request.VenueId, sectionId, cancellationToken) is null)
        {
            throw new ValidationAppException("sectionId", "The section does not belong to this venue.");
        }

        var schedule = await venueService.LoadScheduleAsync(venueRef, request.Date, request.Date, cancellationToken);
        var businessDate = LocalDate.FromDateOnly(request.Date);
        var periods = schedule.GetPeriods(businessDate);
        if (periods.Count == 0 || venue.Status != VenueStatus.Active)
        {
            return new AvailabilityResponse(venue.Id, request.Date, venue.TimeZone, request.PartySize, durationMinutes,
                rules.TurnoverBufferMinutes, IsClosed: true, []);
        }

        var now = Instant.FromDateTimeUtc(DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc));
        var slots = await CalculateAsync(tenantId, venue, schedule, businessDate, request.PartySize, durationMinutes,
            r => (request.SectionId is null || r.SectionId == request.SectionId) && (request.ResourceTypeId is null || r.ResourceTypeId == request.ResourceTypeId),
            onlineOnly: false, now, cancellationToken);

        if (request.Time is { } preferred)
        {
            var preferredInstant = schedule.ToInstant(businessDate, LocalTime.FromTimeOnly(preferred));
            var window = Duration.FromMinutes(TimeWindowMinutes);
            slots = slots.Where(s => s.Start >= preferredInstant - window && s.Start <= preferredInstant + window).ToList();
        }

        return new AvailabilityResponse(venue.Id, request.Date, venue.TimeZone, request.PartySize, durationMinutes,
            rules.TurnoverBufferMinutes, IsClosed: false, slots.Select(ToDto).ToList());
    }

    /// <summary>
    /// Guest-facing slots for a business date: default duration, resources and combinations bookable online only, and no
    /// slot before <paramref name="earliestStart"/> (now + the venue's lead time). Callers enforce the booking window.
    /// </summary>
    public Task<IReadOnlyList<AvailabilitySlot>> CalculateOnlineAsync(
        Guid tenantId, VenueDto venue, OperatingSchedule schedule, LocalDate businessDate, int partySize, Instant earliestStart, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(venue);
        return CalculateAsync(tenantId, venue, schedule, businessDate, partySize, venue.Rules.DefaultDurationMinutes, _ => true,
            onlineOnly: true, earliestStart, cancellationToken);
    }

    private async Task<IReadOnlyList<AvailabilitySlot>> CalculateAsync(
        Guid tenantId,
        VenueDto venue,
        OperatingSchedule schedule,
        LocalDate businessDate,
        int partySize,
        int durationMinutes,
        Func<AvailabilityResourceRow, bool> resourceFilter,
        bool onlineOnly,
        Instant earliestStart,
        CancellationToken cancellationToken)
    {
        var periods = schedule.GetPeriods(businessDate);
        if (periods.Count == 0)
        {
            return [];
        }

        var rules = venue.Rules;
        var resources = (await availability.ListResourcesAsync(tenantId, venue.Id, cancellationToken))
            .Where(r => r.IsActive && (!onlineOnly || r.IsBookableOnline))
            .Where(resourceFilter)
            .Select(r => new AvailabilityResource(r.Id, r.Name, r.MinCapacity, r.Capacity, r.SortOrder, r.SectionId))
            .ToList();
        var combinations = (await availability.ListActiveCombinationsAsync(tenantId, venue.Id, cancellationToken))
            .Where(c => !onlineOnly || c.IsBookableOnline)
            .Select(c => new AvailabilityCombination(c.Id, c.Name, c.MinCapacity, c.Capacity, c.SortOrder,
                c.Members.Select(m => new CombinationMember(m.ResourceId, m.IsRequired)).ToList()))
            .ToList();

        var duration = Duration.FromMinutes(durationMinutes);
        var turnover = Duration.FromMinutes(rules.TurnoverBufferMinutes);
        var windowStart = periods.Min(p => p.Start);
        var windowEnd = periods.Max(p => p.End) + turnover;
        var claims = (await availability.ListClaimsAsync(tenantId, venue.Id, windowStart.ToDateTimeUtc(), windowEnd.ToDateTimeUtc(), clock.UtcNow, null, cancellationToken))
            .Select(c => new ResourceClaim(c.ResourceId, Instant.FromDateTimeUtc(DateTime.SpecifyKind(c.StartsAt, DateTimeKind.Utc)),
                Instant.FromDateTimeUtc(DateTime.SpecifyKind(c.EndsAt, DateTimeKind.Utc))))
            .ToList();

        var query = new AvailabilityQuery(businessDate, partySize, duration, Duration.FromMinutes(rules.SlotIntervalMinutes), turnover, earliestStart);
        return AvailabilityCalculator.Calculate(schedule, query, resources, combinations, claims);
    }

    private static AvailabilitySlotDto ToDto(AvailabilitySlot slot) => new(
        slot.LocalTime.ToTimeOnly(),
        slot.Start.ToDateTimeUtc(),
        slot.End.ToDateTimeUtc(),
        slot.IsAvailable,
        slot.Options.Select(o => new AvailabilityOptionDto(
            o.Kind == AvailabilityOptionKind.Resource ? "RESOURCE" : "COMBINATION", o.Id, o.Name, o.Capacity, o.ResourceIds)).ToList());
}

public sealed class AvailabilityRequestValidator : AbstractValidator<AvailabilityRequest>
{
    public AvailabilityRequestValidator()
    {
        RuleFor(r => r.VenueId).NotEmpty();
        RuleFor(r => r.Date).NotEmpty();
        RuleFor(r => r.PartySize).InclusiveBetween(1, 1000);
        RuleFor(r => r.DurationMinutes).InclusiveBetween(5, 1440).When(r => r.DurationMinutes is not null);
    }
}
