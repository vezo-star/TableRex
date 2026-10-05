namespace BookingPlatform.Application.Availability;

/// <param name="Time">Optional preferred venue-local start time; narrows the result to slots within <see cref="AvailabilityService.TimeWindowMinutes"/> of it.</param>
public sealed record AvailabilityRequest(
    Guid VenueId,
    DateOnly Date,
    int PartySize,
    int? DurationMinutes = null,
    TimeOnly? Time = null,
    Guid? SectionId = null,
    Guid? ResourceTypeId = null);

public sealed record AvailabilityOptionDto(string Kind, Guid Id, string Name, int Capacity, IReadOnlyList<Guid> ResourceIds);

public sealed record AvailabilitySlotDto(TimeOnly Time, DateTime StartsAt, DateTime EndsAt, bool Available, IReadOnlyList<AvailabilityOptionDto> Options);

public sealed record AvailabilityResponse(
    Guid VenueId,
    DateOnly Date,
    string TimeZone,
    int PartySize,
    int DurationMinutes,
    int TurnoverMinutes,
    bool IsClosed,
    IReadOnlyList<AvailabilitySlotDto> Slots);

public sealed record AvailabilityResourceRow(
    Guid Id,
    string Name,
    Guid? SectionId,
    Guid ResourceTypeId,
    int MinCapacity,
    int Capacity,
    bool IsActive,
    bool IsBookableOnline,
    int SortOrder);

public sealed record AvailabilityCombinationRow(
    Guid Id,
    string Name,
    int MinCapacity,
    int Capacity,
    bool IsBookableOnline,
    int SortOrder,
    IReadOnlyList<(Guid ResourceId, bool IsRequired)> Members);

public sealed record ClaimRow(Guid ResourceId, DateTime StartsAt, DateTime EndsAt);

public interface IAvailabilityRepository
{
    Task<IReadOnlyList<AvailabilityResourceRow>> ListResourcesAsync(Guid tenantId, Guid venueId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AvailabilityCombinationRow>> ListActiveCombinationsAsync(Guid tenantId, Guid venueId, CancellationToken cancellationToken);

    /// <summary>Active allocations in the venue overlapping <c>[from, to)</c>, excluding holds that expired at or before <paramref name="now"/> and, optionally, one hold.</summary>
    Task<IReadOnlyList<ClaimRow>> ListClaimsAsync(Guid tenantId, Guid venueId, DateTime from, DateTime to, DateTime now, Guid? excludeHoldId, CancellationToken cancellationToken);
}
