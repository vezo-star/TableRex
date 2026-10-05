using BookingPlatform.Application.Common.Auditing;
using BookingPlatform.Application.Common.Errors;
using BookingPlatform.Application.Common.Persistence;
using BookingPlatform.Application.Common.Security;
using BookingPlatform.Application.Common.Validation;
using BookingPlatform.Domain.Time;
using BookingPlatform.Domain.Venues;
using FluentValidation;
using NodaTime;

namespace BookingPlatform.Application.Venues;

public sealed class VenueService(
    IVenueRepository venues,
    IVenueAccessGuard guard,
    IAuditWriter audit,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    IValidator<CreateVenueRequest> createValidator,
    IValidator<UpdateVenueRequest> updateValidator,
    IValidator<SectionRequest> sectionValidator,
    IValidator<ReplaceOperatingHoursRequest> hoursValidator,
    IValidator<SpecialHoursRequest> specialValidator)
{
    public const int MaxScheduleDays = 62;

    public async Task<IReadOnlyList<VenueDto>> ListAsync(CancellationToken cancellationToken)
    {
        var accessible = await guard.GetAccessibleVenueIdsAsync(cancellationToken);
        return await venues.ListAsync(tenantContext.TenantId, accessible, cancellationToken);
    }

    public async Task<VenueDto> GetAsync(Guid venueId, CancellationToken cancellationToken)
    {
        await guard.EnsureAccessAsync(venueId, cancellationToken);
        return (await venues.GetAsync(tenantContext.TenantId, venueId, cancellationToken))!;
    }

    public async Task<VenueDto> CreateAsync(CreateVenueRequest request, CancellationToken cancellationToken)
    {
        await createValidator.EnsureValidAsync(request, cancellationToken);
        var tenantId = tenantContext.TenantId;
        var normalised = Normalise(request);
        var rules = request.Rules ?? VenueBookingRules.Default;

        return await unitOfWork.ExecuteAsync(async () =>
        {
            if (await venues.SlugExistsAsync(tenantId, normalised.Slug, cancellationToken))
            {
                throw new ConflictAppException(ErrorCodes.SlugTaken, "Another venue already uses that slug.");
            }

            var venueId = await venues.CreateAsync(tenantId, normalised, rules, cancellationToken);
            var created = (await venues.GetAsync(tenantId, venueId, cancellationToken))!;
            await audit.WriteAsync(new AuditEntry(tenantId, "venue", venueId, AuditActions.Created, NewValues: created), cancellationToken);
            return created;
        }, cancellationToken);
    }

    public async Task<VenueDto> UpdateAsync(Guid venueId, UpdateVenueRequest request, CancellationToken cancellationToken)
    {
        await updateValidator.EnsureValidAsync(request, cancellationToken);
        await guard.EnsureAccessAsync(venueId, cancellationToken);
        var tenantId = tenantContext.TenantId;

        return await unitOfWork.ExecuteAsync(async () =>
        {
            var before = (await venues.GetAsync(tenantId, venueId, cancellationToken))!;
            await venues.UpdateAsync(tenantId, venueId, Normalise(request), cancellationToken);
            var after = (await venues.GetAsync(tenantId, venueId, cancellationToken))!;
            await audit.WriteAsync(new AuditEntry(tenantId, "venue", venueId, AuditActions.Updated, before, after), cancellationToken);
            return after;
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<SectionDto>> ListSectionsAsync(Guid venueId, CancellationToken cancellationToken)
    {
        await guard.EnsureAccessAsync(venueId, cancellationToken);
        return await venues.ListSectionsAsync(tenantContext.TenantId, venueId, cancellationToken);
    }

    public async Task<SectionDto> CreateSectionAsync(Guid venueId, SectionRequest request, CancellationToken cancellationToken)
    {
        await sectionValidator.EnsureValidAsync(request, cancellationToken);
        await guard.EnsureAccessAsync(venueId, cancellationToken);
        var tenantId = tenantContext.TenantId;
        var name = request.Name.Trim();

        return await unitOfWork.ExecuteAsync(async () =>
        {
            if (await venues.SectionNameExistsAsync(tenantId, venueId, name, null, cancellationToken))
            {
                throw new ConflictAppException(ErrorCodes.Conflict, "A section with that name already exists in this venue.");
            }

            var sectionId = await venues.CreateSectionAsync(tenantId, venueId, request with { Name = name }, cancellationToken);
            var created = (await venues.GetSectionAsync(tenantId, venueId, sectionId, cancellationToken))!;
            await audit.WriteAsync(new AuditEntry(tenantId, "venue_section", sectionId, AuditActions.Created, NewValues: created), cancellationToken);
            return created;
        }, cancellationToken);
    }

    public async Task<SectionDto> UpdateSectionAsync(Guid venueId, Guid sectionId, SectionRequest request, CancellationToken cancellationToken)
    {
        await sectionValidator.EnsureValidAsync(request, cancellationToken);
        await guard.EnsureAccessAsync(venueId, cancellationToken);
        var tenantId = tenantContext.TenantId;
        var name = request.Name.Trim();

        return await unitOfWork.ExecuteAsync(async () =>
        {
            var before = await venues.GetSectionAsync(tenantId, venueId, sectionId, cancellationToken)
                ?? throw new NotFoundAppException(ErrorCodes.SectionNotFound, "The section was not found.");
            if (await venues.SectionNameExistsAsync(tenantId, venueId, name, sectionId, cancellationToken))
            {
                throw new ConflictAppException(ErrorCodes.Conflict, "A section with that name already exists in this venue.");
            }

            await venues.UpdateSectionAsync(tenantId, venueId, sectionId, request with { Name = name }, cancellationToken);
            var after = (await venues.GetSectionAsync(tenantId, venueId, sectionId, cancellationToken))!;
            await audit.WriteAsync(new AuditEntry(tenantId, "venue_section", sectionId, AuditActions.Updated, before, after), cancellationToken);
            return after;
        }, cancellationToken);
    }

    public async Task<DeleteResult> DeleteSectionAsync(Guid venueId, Guid sectionId, CancellationToken cancellationToken)
    {
        await guard.EnsureAccessAsync(venueId, cancellationToken);
        var tenantId = tenantContext.TenantId;

        return await unitOfWork.ExecuteAsync(async () =>
        {
            var before = await venues.GetSectionAsync(tenantId, venueId, sectionId, cancellationToken)
                ?? throw new NotFoundAppException(ErrorCodes.SectionNotFound, "The section was not found.");
            var result = await venues.DeleteOrDeactivateSectionAsync(tenantId, venueId, sectionId, cancellationToken);
            await audit.WriteAsync(new AuditEntry(tenantId, "venue_section", sectionId,
                result.Deleted ? AuditActions.Deleted : AuditActions.Deactivated, before, null), cancellationToken);
            return result;
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<OperatingHoursPeriod>> GetOperatingHoursAsync(Guid venueId, CancellationToken cancellationToken)
    {
        await guard.EnsureAccessAsync(venueId, cancellationToken);
        return await venues.ListOperatingHoursAsync(tenantContext.TenantId, venueId, cancellationToken);
    }

    public async Task<IReadOnlyList<OperatingHoursPeriod>> ReplaceOperatingHoursAsync(
        Guid venueId, ReplaceOperatingHoursRequest request, CancellationToken cancellationToken)
    {
        await hoursValidator.EnsureValidAsync(request, cancellationToken);
        var weekly = request.Periods
            .Select(p => new WeeklyPeriod((IsoDayOfWeek)p.DayOfWeek, LocalTime.FromTimeOnly(p.OpenTime), LocalTime.FromTimeOnly(p.CloseTime)))
            .ToList();
        if (OperatingSchedule.ValidateWeekly(weekly) is { } error)
        {
            throw new ValidationAppException("periods", error);
        }

        await guard.EnsureAccessAsync(venueId, cancellationToken);
        var tenantId = tenantContext.TenantId;
        var ordered = request.Periods.OrderBy(p => p.DayOfWeek).ThenBy(p => p.OpenTime).ToList();

        return await unitOfWork.ExecuteAsync(async () =>
        {
            await venues.LockAsync(tenantId, venueId, cancellationToken);
            var before = await venues.ListOperatingHoursAsync(tenantId, venueId, cancellationToken);
            await venues.ReplaceOperatingHoursAsync(tenantId, venueId, ordered, cancellationToken);
            await audit.WriteAsync(new AuditEntry(tenantId, "venue", venueId, AuditActions.Updated,
                new { operatingHours = before }, new { operatingHours = ordered }), cancellationToken);
            return (IReadOnlyList<OperatingHoursPeriod>)ordered;
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<SpecialHoursDto>> ListSpecialHoursAsync(Guid venueId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        await guard.EnsureAccessAsync(venueId, cancellationToken);
        return await venues.ListSpecialHoursAsync(tenantContext.TenantId, venueId, from, to, cancellationToken);
    }

    public async Task<SpecialHoursDto> CreateSpecialHoursAsync(Guid venueId, SpecialHoursRequest request, CancellationToken cancellationToken)
    {
        await specialValidator.EnsureValidAsync(request, cancellationToken);
        await guard.EnsureAccessAsync(venueId, cancellationToken);
        var tenantId = tenantContext.TenantId;
        var normalised = request with { Name = TrimToNull(request.Name) };

        return await unitOfWork.ExecuteAsync(async () =>
        {
            await venues.LockAsync(tenantId, venueId, cancellationToken);
            await EnsureCompatibleWithDateAsync(tenantId, venueId, normalised, exceptId: null, cancellationToken);
            var id = await venues.CreateSpecialHoursAsync(tenantId, venueId, normalised, cancellationToken);
            var created = (await venues.GetSpecialHoursAsync(tenantId, venueId, id, cancellationToken))!;
            await audit.WriteAsync(new AuditEntry(tenantId, "special_operating_hours", id, AuditActions.Created, NewValues: created), cancellationToken);
            return created;
        }, cancellationToken);
    }

    public async Task<SpecialHoursDto> UpdateSpecialHoursAsync(Guid venueId, Guid id, SpecialHoursRequest request, CancellationToken cancellationToken)
    {
        await specialValidator.EnsureValidAsync(request, cancellationToken);
        await guard.EnsureAccessAsync(venueId, cancellationToken);
        var tenantId = tenantContext.TenantId;
        var normalised = request with { Name = TrimToNull(request.Name) };

        return await unitOfWork.ExecuteAsync(async () =>
        {
            await venues.LockAsync(tenantId, venueId, cancellationToken);
            var before = await venues.GetSpecialHoursAsync(tenantId, venueId, id, cancellationToken)
                ?? throw new NotFoundAppException(ErrorCodes.SpecialHoursNotFound, "The special hours entry was not found.");
            await EnsureCompatibleWithDateAsync(tenantId, venueId, normalised, exceptId: id, cancellationToken);
            await venues.UpdateSpecialHoursAsync(tenantId, venueId, id, normalised, cancellationToken);
            var after = (await venues.GetSpecialHoursAsync(tenantId, venueId, id, cancellationToken))!;
            await audit.WriteAsync(new AuditEntry(tenantId, "special_operating_hours", id, AuditActions.Updated, before, after), cancellationToken);
            return after;
        }, cancellationToken);
    }

    public async Task DeleteSpecialHoursAsync(Guid venueId, Guid id, CancellationToken cancellationToken)
    {
        await guard.EnsureAccessAsync(venueId, cancellationToken);
        var tenantId = tenantContext.TenantId;

        await unitOfWork.ExecuteAsync(async () =>
        {
            var before = await venues.GetSpecialHoursAsync(tenantId, venueId, id, cancellationToken)
                ?? throw new NotFoundAppException(ErrorCodes.SpecialHoursNotFound, "The special hours entry was not found.");
            await venues.DeleteSpecialHoursAsync(tenantId, venueId, id, cancellationToken);
            await audit.WriteAsync(new AuditEntry(tenantId, "special_operating_hours", id, AuditActions.Deleted, before, null), cancellationToken);
        }, cancellationToken);
    }

    /// <summary>Effective opening periods per business date in <c>[from, to]</c> (venue-local dates).</summary>
    public async Task<VenueScheduleDto> GetScheduleAsync(Guid venueId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (to < from)
        {
            throw new ValidationAppException("to", "The end date must be on or after the start date.");
        }

        if (to.DayNumber - from.DayNumber + 1 > MaxScheduleDays)
        {
            throw new ValidationAppException("to", $"Request at most {MaxScheduleDays} days at a time.");
        }

        var venue = await guard.EnsureAccessAsync(venueId, cancellationToken);
        var schedule = await LoadScheduleAsync(venue, from, to, cancellationToken);

        var days = new List<ScheduleDayDto>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var local = LocalDate.FromDateOnly(date);
            var periods = schedule.GetPeriods(local)
                .Select(p => new SchedulePeriodDto(p.Open.ToTimeOnly(), p.Close.ToTimeOnly(), p.Start.ToDateTimeUtc(), p.End.ToDateTimeUtc(), p.IsOvernight))
                .ToList();
            days.Add(new ScheduleDayDto(date, schedule.IsSpecialDate(local), periods.Count == 0, periods));
        }

        return new VenueScheduleDto(venueId, venue.TimeZone, days);
    }

    /// <summary>Builds the domain schedule for a venue, including special dates in the window (plus one day either side for overnight periods).</summary>
    public async Task<OperatingSchedule> LoadScheduleAsync(VenueRef venue, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId;
        var weekly = await venues.ListOperatingHoursAsync(tenantId, venue.Id, cancellationToken);
        var special = await venues.ListSpecialHoursAsync(tenantId, venue.Id, from.AddDays(-1), to.AddDays(1), cancellationToken);
        return new OperatingSchedule(
            TimeZones.Get(venue.TimeZone),
            weekly.Select(p => new WeeklyPeriod((IsoDayOfWeek)p.DayOfWeek, LocalTime.FromTimeOnly(p.OpenTime), LocalTime.FromTimeOnly(p.CloseTime))),
            special.Select(s => new SpecialPeriod(LocalDate.FromDateOnly(s.Date), s.IsClosed,
                s.OpenTime is { } open ? LocalTime.FromTimeOnly(open) : null,
                s.CloseTime is { } close ? LocalTime.FromTimeOnly(close) : null)));
    }

    /// <summary>A date holds either one closed row or non-overlapping custom periods.</summary>
    private async Task EnsureCompatibleWithDateAsync(Guid tenantId, Guid venueId, SpecialHoursRequest request, Guid? exceptId, CancellationToken cancellationToken)
    {
        var others = (await venues.ListSpecialHoursAsync(tenantId, venueId, request.Date, request.Date, cancellationToken))
            .Where(s => s.Id != exceptId)
            .ToList();
        if (others.Count == 0)
        {
            return;
        }

        if (request.IsClosed || others.Any(s => s.IsClosed))
        {
            throw new ConflictAppException(ErrorCodes.Conflict,
                "This date already has special hours. A closed day cannot be combined with opening periods; edit or remove the existing entry.");
        }

        var periods = others
            .Select(s => (LocalTime.FromTimeOnly(s.OpenTime!.Value), LocalTime.FromTimeOnly(s.CloseTime!.Value)))
            .Append((LocalTime.FromTimeOnly(request.OpenTime!.Value), LocalTime.FromTimeOnly(request.CloseTime!.Value)))
            .ToList();
        if (OperatingSchedule.ValidateSameDay(periods) is { } error)
        {
            throw new ValidationAppException("openTime", error);
        }
    }

    private static CreateVenueRequest Normalise(CreateVenueRequest r) => r with
    {
        Name = r.Name.Trim(),
        Description = TrimToNull(r.Description),
        AddressLine1 = TrimToNull(r.AddressLine1),
        AddressLine2 = TrimToNull(r.AddressLine2),
        City = TrimToNull(r.City),
        Region = TrimToNull(r.Region),
        PostalCode = TrimToNull(r.PostalCode),
        CountryCode = TrimToNull(r.CountryCode),
        Phone = TrimToNull(r.Phone),
        Email = TrimToNull(r.Email),
    };

    private static UpdateVenueRequest Normalise(UpdateVenueRequest r) => r with
    {
        Name = r.Name.Trim(),
        Description = TrimToNull(r.Description),
        AddressLine1 = TrimToNull(r.AddressLine1),
        AddressLine2 = TrimToNull(r.AddressLine2),
        City = TrimToNull(r.City),
        Region = TrimToNull(r.Region),
        PostalCode = TrimToNull(r.PostalCode),
        CountryCode = TrimToNull(r.CountryCode),
        Phone = TrimToNull(r.Phone),
        Email = TrimToNull(r.Email),
    };

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
