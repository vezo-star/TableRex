using BookingPlatform.Domain.Venues;
using NodaTime;

namespace BookingPlatform.Domain.Availability;

/// <summary>A resource that may be offered (already filtered to active, in-venue and matching the caller's filters).</summary>
public sealed record AvailabilityResource(Guid Id, string Name, int MinCapacity, int Capacity, int SortOrder, Guid? SectionId);

public sealed record CombinationMember(Guid ResourceId, bool IsRequired);

/// <summary>An active combination. Members that are not in the eligible resource set make a required member unavailable.</summary>
public sealed record AvailabilityCombination(Guid Id, string Name, int MinCapacity, int Capacity, int SortOrder, IReadOnlyList<CombinationMember> Members);

/// <summary>An active time claim (booking, unexpired hold or block) on a resource; half-open <c>[Start, End)</c>.</summary>
public sealed record ResourceClaim(Guid ResourceId, Instant Start, Instant End);

public enum AvailabilityOptionKind
{
    Resource,
    Combination,
}

/// <summary>A bookable choice for a slot. For combinations, <see cref="ResourceIds"/> lists the members that would be allocated.</summary>
public sealed record AvailabilityOption(AvailabilityOptionKind Kind, Guid Id, string Name, int Capacity, int SortOrder, IReadOnlyList<Guid> ResourceIds);

public sealed record AvailabilitySlot(Instant Start, Instant End, LocalTime LocalTime, IReadOnlyList<AvailabilityOption> Options)
{
    public bool IsAvailable => Options.Count > 0;
}

/// <param name="BusinessDate">Venue-local business date whose operating periods are searched.</param>
/// <param name="Duration">Guest-facing duration; the slot must fit inside an operating period.</param>
/// <param name="Turnover">Added after the duration when checking conflicts (cleaning/reset buffer).</param>
/// <param name="EarliestStart">Slots starting before this instant are omitted (now, or now + lead time for public requests).</param>
public sealed record AvailabilityQuery(
    LocalDate BusinessDate,
    int PartySize,
    Duration Duration,
    Duration SlotInterval,
    Duration Turnover,
    Instant? EarliestStart);

/// <summary>
/// Pure availability search (DOMAIN_MODEL §7). Candidate slots start at each operating period's opening and repeat
/// every slot interval while <c>[t, t + duration)</c> fits the period. A resource is free when no claim overlaps
/// <c>[t, t + duration + turnover)</c>; a combination is free when all its required members are free.
/// </summary>
public static class AvailabilityCalculator
{
    public static IReadOnlyList<AvailabilitySlot> Calculate(
        OperatingSchedule schedule,
        AvailabilityQuery query,
        IReadOnlyCollection<AvailabilityResource> resources,
        IReadOnlyCollection<AvailabilityCombination> combinations,
        IReadOnlyCollection<ResourceClaim> claims)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(query);
        if (query.PartySize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Party size must be positive.");
        }

        if (query.Duration <= Duration.Zero || query.SlotInterval <= Duration.Zero || query.Turnover < Duration.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Duration and slot interval must be positive and turnover non-negative.");
        }

        var resourcesById = resources.ToDictionary(r => r.Id);
        var singles = resources
            .Where(r => r.MinCapacity <= query.PartySize && query.PartySize <= r.Capacity)
            .ToList();
        var eligibleCombinations = combinations
            .Where(c => c.MinCapacity <= query.PartySize && query.PartySize <= c.Capacity)
            .Where(c => c.Members.Any(m => m.IsRequired) && c.Members.Where(m => m.IsRequired).All(m => resourcesById.ContainsKey(m.ResourceId)))
            .ToList();
        var claimsByResource = claims.ToLookup(c => c.ResourceId);

        var slots = new List<AvailabilitySlot>();
        foreach (var period in schedule.GetPeriods(query.BusinessDate))
        {
            for (var start = period.Start; start + query.Duration <= period.End; start += query.SlotInterval)
            {
                if (query.EarliestStart is { } earliest && start < earliest)
                {
                    continue;
                }

                var end = start + query.Duration;
                var claimEnd = end + query.Turnover;
                bool IsFree(Guid resourceId) => !claimsByResource[resourceId].Any(c => c.Start < claimEnd && c.End > start);

                var options = new List<AvailabilityOption>();
                options.AddRange(singles
                    .Where(r => IsFree(r.Id))
                    .Select(r => new AvailabilityOption(AvailabilityOptionKind.Resource, r.Id, r.Name, r.Capacity, r.SortOrder, [r.Id])));

                foreach (var combination in eligibleCombinations)
                {
                    if (!combination.Members.Where(m => m.IsRequired).All(m => IsFree(m.ResourceId)))
                    {
                        continue;
                    }

                    var allocated = combination.Members
                        .Where(m => m.IsRequired || (resourcesById.ContainsKey(m.ResourceId) && IsFree(m.ResourceId)))
                        .Select(m => m.ResourceId)
                        .ToList();
                    options.Add(new AvailabilityOption(AvailabilityOptionKind.Combination, combination.Id, combination.Name,
                        combination.Capacity, combination.SortOrder, allocated));
                }

                slots.Add(new AvailabilitySlot(start, end, start.InZone(schedule.Zone).TimeOfDay, Rank(options)));
            }
        }

        return slots;
    }

    /// <summary>Single resources before combinations, then smallest sufficient capacity, then sort order, then name.</summary>
    public static IReadOnlyList<AvailabilityOption> Rank(IEnumerable<AvailabilityOption> options) =>
        options
            .OrderBy(o => o.Kind)
            .ThenBy(o => o.Capacity)
            .ThenBy(o => o.SortOrder)
            .ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(o => o.Id)
            .ToList();
}
