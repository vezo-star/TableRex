using System.Security.Cryptography;
using System.Text;
using BookingPlatform.Application.Availability;
using BookingPlatform.Application.Common.Errors;
using BookingPlatform.Application.Venues;
using BookingPlatform.Domain.Availability;
using BookingPlatform.Domain.Venues;
using NodaTime;
using IClock = BookingPlatform.Application.Common.Time.IClock;

namespace BookingPlatform.Application.Bookings;

/// <summary>A resolved booking window. <see cref="End"/> excludes turnover; allocations end at <see cref="ClaimEnd"/>.</summary>
public sealed record BookingTiming(Instant Start, Instant End, Instant ClaimEnd, LocalDate BusinessDate, int DurationMinutes)
{
    public DateTime StartUtc => Start.ToDateTimeUtc();

    public DateTime EndUtc => End.ToDateTimeUtc();

    public DateTime ClaimEndUtc => ClaimEnd.ToDateTimeUtc();

    public DateOnly BusinessDateOnly => BusinessDate.ToDateOnly();
}

/// <summary>One set of resources to try; <see cref="CombinationId"/> is set when allocated through a combination.</summary>
public sealed record AllocationCandidate(IReadOnlyList<Guid> ResourceIds, Guid? CombinationId);

/// <summary>
/// Chooses and claims resources for bookings and holds. Claims are inserted ordered by resource id inside savepoints;
/// the exclusion constraint is the final arbiter (DATABASE.md §4). AUTO tries the ranked options in turn.
/// </summary>
public sealed class BookingAllocator(
    IAvailabilityRepository availability,
    IAllocatableResourceRepository resources,
    IAllocationRepository allocations,
    IClock clock)
{
    public const int MaxAutoAttempts = 20;
    public const string NotAvailableMessage = "One or more selected resources are no longer available for the requested time.";

    /// <summary>
    /// Resolves venue-local <paramref name="date"/> + <paramref name="time"/> to a window inside one operating period of
    /// that business date. After-midnight times fall into the date's overnight period. Staff never override hours.
    /// </summary>
    public static BookingTiming ResolveTiming(OperatingSchedule schedule, VenueBookingRules rules, DateOnly date, TimeOnly time, int? durationMinutes)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(rules);

        var duration = durationMinutes ?? rules.DefaultDurationMinutes;
        if (duration < rules.MinDurationMinutes || duration > rules.MaxDurationMinutes)
        {
            throw new ValidationAppException("durationMinutes",
                $"Duration must be between {rules.MinDurationMinutes} and {rules.MaxDurationMinutes} minutes for this venue.");
        }

        var businessDate = LocalDate.FromDateOnly(date);
        var periods = schedule.GetPeriods(businessDate);
        if (periods.Count == 0)
        {
            throw new BusinessRuleAppException(ErrorCodes.OutsideOperatingHours, "The venue is closed on the requested date.");
        }

        var localTime = LocalTime.FromTimeOnly(time);
        foreach (var dayOffset in new[] { 0, 1 })
        {
            var start = schedule.ToInstant(businessDate.PlusDays(dayOffset), localTime);
            var end = start + Duration.FromMinutes(duration);
            if (periods.Any(p => p.Contains(start, end)))
            {
                return new BookingTiming(start, end, end + Duration.FromMinutes(rules.TurnoverBufferMinutes), businessDate, duration);
            }
        }

        throw new BusinessRuleAppException(ErrorCodes.OutsideOperatingHours,
            "The booking must start and end within the venue's operating hours.");
    }

    /// <summary>Candidate resource sets for the request, best first. Explicit choices yield exactly one candidate.</summary>
    public async Task<IReadOnlyList<AllocationCandidate>> PlanAsync(
        Guid tenantId, Guid venueId, AllocationRequest? request, int partySize, BookingTiming timing, Guid? excludeHoldId, CancellationToken cancellationToken)
    {
        var mode = request?.Mode ?? AllocationModes.Auto;
        return mode switch
        {
            AllocationModes.Resources => [await PlanResourcesAsync(tenantId, venueId, request!.ResourceIds ?? [], partySize, cancellationToken)],
            AllocationModes.Combination => [await PlanCombinationAsync(tenantId, venueId, request!.CombinationId!.Value, partySize, timing, excludeHoldId, cancellationToken)],
            _ => await PlanAutoAsync(tenantId, venueId, partySize, timing, excludeHoldId, onlineOnly: false, cancellationToken),
        };
    }

    /// <summary>AUTO allocation restricted to resources and combinations that are bookable online (public bookings).</summary>
    public Task<IReadOnlyList<AllocationCandidate>> PlanOnlineAsync(Guid tenantId, Guid venueId, int partySize, BookingTiming timing, CancellationToken cancellationToken) =>
        PlanAutoAsync(tenantId, venueId, partySize, timing, null, onlineOnly: true, cancellationToken);

    /// <summary>Validates explicit resources: same venue, active, and able to seat the party.</summary>
    public async Task<AllocationCandidate> PlanResourcesAsync(Guid tenantId, Guid venueId, IReadOnlyList<Guid> resourceIds, int partySize, CancellationToken cancellationToken)
    {
        var distinct = resourceIds.Distinct().ToList();
        var found = await resources.GetManyAsync(tenantId, distinct, cancellationToken);
        if (distinct.Count == 0 || found.Count != distinct.Count || found.Any(r => r.VenueId != venueId))
        {
            throw new NotFoundAppException(ErrorCodes.ResourceNotFound, "One or more resources were not found.");
        }

        if (found.Any(r => !r.IsActive))
        {
            throw new ValidationAppException("allocation.resourceIds", "Inactive resources cannot be booked.");
        }

        var fits = found.Count == 1
            ? found[0].MinCapacity <= partySize && partySize <= found[0].Capacity
            : partySize <= found.Sum(r => r.Capacity);
        if (!fits)
        {
            throw new BusinessRuleAppException(ErrorCodes.PartySizeNotSupported, "The selected resources cannot seat this party size.");
        }

        return new AllocationCandidate(distinct.Order().ToList(), null);
    }

    private async Task<AllocationCandidate> PlanCombinationAsync(
        Guid tenantId, Guid venueId, Guid combinationId, int partySize, BookingTiming timing, Guid? excludeHoldId, CancellationToken cancellationToken)
    {
        var combination = (await availability.ListActiveCombinationsAsync(tenantId, venueId, cancellationToken)).FirstOrDefault(c => c.Id == combinationId)
            ?? throw new NotFoundAppException(ErrorCodes.CombinationNotFound, "The combination was not found.");
        if (partySize < combination.MinCapacity || partySize > combination.Capacity)
        {
            throw new BusinessRuleAppException(ErrorCodes.PartySizeNotSupported, "The combination cannot seat this party size.");
        }

        var active = (await availability.ListResourcesAsync(tenantId, venueId, cancellationToken)).Where(r => r.IsActive).Select(r => r.Id).ToHashSet();
        var required = combination.Members.Where(m => m.IsRequired).Select(m => m.ResourceId).ToList();
        if (required.Any(id => !active.Contains(id)))
        {
            throw new BusinessRuleAppException(ErrorCodes.ResourceNotAvailable, "A required member of the combination is inactive.");
        }

        var optional = combination.Members.Where(m => !m.IsRequired && active.Contains(m.ResourceId)).Select(m => m.ResourceId).ToList();
        if (optional.Count > 0)
        {
            var claims = await LoadClaimsAsync(tenantId, venueId, timing, excludeHoldId, cancellationToken);
            optional = optional.Where(id => !claims.Any(c => c.ResourceId == id && c.Start < timing.ClaimEnd && c.End > timing.Start)).ToList();
        }

        return new AllocationCandidate(required.Concat(optional).Order().ToList(), combination.Id);
    }

    private async Task<IReadOnlyList<AllocationCandidate>> PlanAutoAsync(
        Guid tenantId, Guid venueId, int partySize, BookingTiming timing, Guid? excludeHoldId, bool onlineOnly, CancellationToken cancellationToken)
    {
        var resourceRows = (await availability.ListResourcesAsync(tenantId, venueId, cancellationToken))
            .Where(r => r.IsActive && (!onlineOnly || r.IsBookableOnline))
            .ToList();
        var candidates = resourceRows
            .Select(r => new AvailabilityResource(r.Id, r.Name, r.MinCapacity, r.Capacity, r.SortOrder, r.SectionId))
            .ToList();
        var combinations = (await availability.ListActiveCombinationsAsync(tenantId, venueId, cancellationToken))
            .Where(c => !onlineOnly || c.IsBookableOnline)
            .Select(c => new AvailabilityCombination(c.Id, c.Name, c.MinCapacity, c.Capacity, c.SortOrder,
                c.Members.Select(m => new CombinationMember(m.ResourceId, m.IsRequired)).ToList()))
            .ToList();

        var anyFits = candidates.Any(r => r.MinCapacity <= partySize && partySize <= r.Capacity)
            || combinations.Any(c => c.MinCapacity <= partySize && partySize <= c.Capacity);
        if (!anyFits)
        {
            throw new BusinessRuleAppException(ErrorCodes.PartySizeNotSupported, "No resource or combination at this venue can seat this party size.");
        }

        var claims = await LoadClaimsAsync(tenantId, venueId, timing, excludeHoldId, cancellationToken);
        var options = AvailabilityCalculator.OptionsAt(timing.Start, timing.ClaimEnd, partySize, candidates, combinations, claims);
        if (options.Count == 0)
        {
            throw NotAvailable([]);
        }

        return options
            .Take(MaxAutoAttempts)
            .Select(o => new AllocationCandidate(o.ResourceIds.Order().ToList(), o.Kind == AvailabilityOptionKind.Combination ? o.Id : null))
            .ToList();
    }

    /// <summary>
    /// Claims the first candidate that the exclusion constraint accepts. Expired holds on the affected resources are
    /// released first. When every candidate fails: 409 with the conflicting resource ids for explicit choices.
    /// </summary>
    public async Task<AllocationCandidate> AllocateAsync(
        Guid tenantId,
        IReadOnlyList<AllocationCandidate> candidates,
        Func<AllocationCandidate, AllocationInsert> toInsert,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(toInsert);
        if (candidates.Count == 0)
        {
            throw NotAvailable([]);
        }

        var now = clock.UtcNow;
        var first = toInsert(candidates[0]);
        var allResources = candidates.SelectMany(c => c.ResourceIds).Distinct().ToList();
        await allocations.LockResourcesAsync(allResources, cancellationToken);
        await allocations.ReleaseExpiredHoldsAsync(tenantId, allResources, first.StartsAt, first.EndsAt, now, cancellationToken);

        foreach (var candidate in candidates)
        {
            if (await allocations.TryInsertAsync(toInsert(candidate), cancellationToken))
            {
                return candidate;
            }
        }

        if (candidates.Count == 1)
        {
            var conflicting = await allocations.FindConflictingResourcesAsync(tenantId, candidates[0].ResourceIds, first.StartsAt, first.EndsAt, now, cancellationToken);
            throw NotAvailable(conflicting);
        }

        throw NotAvailable([]);
    }

    public static ConflictAppException NotAvailable(IReadOnlyList<Guid> conflictingResourceIds) =>
        new(ErrorCodes.ResourceNotAvailable, NotAvailableMessage)
        {
            Extensions = new Dictionary<string, object?> { ["conflictingResourceIds"] = conflictingResourceIds },
        };

    private async Task<List<ResourceClaim>> LoadClaimsAsync(Guid tenantId, Guid venueId, BookingTiming timing, Guid? excludeHoldId, CancellationToken cancellationToken) =>
        (await availability.ListClaimsAsync(tenantId, venueId, timing.StartUtc, timing.ClaimEndUtc, clock.UtcNow, excludeHoldId, cancellationToken))
        .Select(c => new ResourceClaim(c.ResourceId, ToInstant(c.StartsAt), ToInstant(c.EndsAt)))
        .ToList();

    private static Instant ToInstant(DateTime value) => Instant.FromDateTimeUtc(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}

/// <summary>Hold tokens: 32 random bytes (base64url) returned once; only the SHA-256 is stored.</summary>
public static class HoldTokens
{
    public static (string Token, byte[] Hash) Generate()
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token));
    }

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
