using BookingPlatform.Application.Common.Pagination;

namespace BookingPlatform.Application.Venues;

public sealed record VenueBookingRules(
    int SlotIntervalMinutes,
    int DefaultDurationMinutes,
    int MinDurationMinutes,
    int MaxDurationMinutes,
    int TurnoverBufferMinutes,
    int MinPartySize,
    int MaxPartySize,
    int MinLeadTimeMinutes,
    int MaxAdvanceDays,
    int HoldTtlSeconds,
    bool AutoConfirmPublicBookings,
    bool PublicBookingEnabled = true,
    int PublicCancellationCutoffMinutes = 120)
{
    public static VenueBookingRules Default { get; } = new(15, 120, 30, 480, 0, 1, 12, 60, 90, 300, true);
}

public sealed record VenueDto(
    Guid Id,
    string Name,
    string Slug,
    string TimeZone,
    string Status,
    string? Description,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? Region,
    string? PostalCode,
    string? CountryCode,
    string? Phone,
    string? Email,
    VenueBookingRules Rules,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CreateVenueRequest(
    string Name,
    string Slug,
    string TimeZone,
    string? Description,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? Region,
    string? PostalCode,
    string? CountryCode,
    string? Phone,
    string? Email,
    VenueBookingRules? Rules);

public sealed record UpdateVenueRequest(
    string Name,
    string TimeZone,
    string Status,
    string? Description,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? Region,
    string? PostalCode,
    string? CountryCode,
    string? Phone,
    string? Email,
    VenueBookingRules Rules);

/// <summary>Minimal venue facts needed by guards and calculators.</summary>
public sealed record VenueRef(Guid Id, string TimeZone, string Status);

public sealed record SectionDto(Guid Id, Guid VenueId, string Name, string? Description, int SortOrder, bool IsActive);

public sealed record SectionRequest(string Name, string? Description, int SortOrder, bool IsActive = true);

public sealed record OperatingHoursPeriod(int DayOfWeek, TimeOnly OpenTime, TimeOnly CloseTime);

public sealed record ReplaceOperatingHoursRequest(IReadOnlyList<OperatingHoursPeriod> Periods);

public sealed record SpecialHoursDto(Guid Id, DateOnly Date, string? Name, bool IsClosed, TimeOnly? OpenTime, TimeOnly? CloseTime);

public sealed record SpecialHoursRequest(DateOnly Date, string? Name, bool IsClosed, TimeOnly? OpenTime, TimeOnly? CloseTime);

public sealed record SchedulePeriodDto(TimeOnly OpenTime, TimeOnly CloseTime, DateTime StartsAt, DateTime EndsAt, bool Overnight);

public sealed record ScheduleDayDto(DateOnly Date, bool IsSpecial, bool IsClosed, IReadOnlyList<SchedulePeriodDto> Periods);

public sealed record VenueScheduleDto(Guid VenueId, string TimeZone, IReadOnlyList<ScheduleDayDto> Days);

public sealed record DeleteResult(bool Deleted, bool Deactivated);

public sealed record UpdateStaffVenuesRequest(IReadOnlyList<Guid> VenueIds);

public interface IVenueRepository
{
    Task<IReadOnlyList<VenueDto>> ListAsync(Guid tenantId, IReadOnlyCollection<Guid>? venueIds, CancellationToken cancellationToken);

    Task<VenueDto?> GetAsync(Guid tenantId, Guid venueId, CancellationToken cancellationToken);

    Task<VenueRef?> GetRefAsync(Guid tenantId, Guid venueId, CancellationToken cancellationToken);

    Task<bool> SlugExistsAsync(Guid tenantId, string slug, CancellationToken cancellationToken);

    Task<int> CountAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<Guid> CreateAsync(Guid tenantId, CreateVenueRequest request, VenueBookingRules rules, CancellationToken cancellationToken);

    Task UpdateAsync(Guid tenantId, Guid venueId, UpdateVenueRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<SectionDto>> ListSectionsAsync(Guid tenantId, Guid venueId, CancellationToken cancellationToken);

    Task<SectionDto?> GetSectionAsync(Guid tenantId, Guid venueId, Guid sectionId, CancellationToken cancellationToken);

    Task<bool> SectionNameExistsAsync(Guid tenantId, Guid venueId, string name, Guid? exceptId, CancellationToken cancellationToken);

    Task<Guid> CreateSectionAsync(Guid tenantId, Guid venueId, SectionRequest request, CancellationToken cancellationToken);

    Task UpdateSectionAsync(Guid tenantId, Guid venueId, Guid sectionId, SectionRequest request, CancellationToken cancellationToken);

    /// <summary>Deletes the section, or deactivates it when other rows still reference it.</summary>
    Task<DeleteResult> DeleteOrDeactivateSectionAsync(Guid tenantId, Guid venueId, Guid sectionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<OperatingHoursPeriod>> ListOperatingHoursAsync(Guid tenantId, Guid venueId, CancellationToken cancellationToken);

    Task ReplaceOperatingHoursAsync(Guid tenantId, Guid venueId, IReadOnlyList<OperatingHoursPeriod> periods, CancellationToken cancellationToken);

    Task<IReadOnlyList<SpecialHoursDto>> ListSpecialHoursAsync(Guid tenantId, Guid venueId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken);

    Task<SpecialHoursDto?> GetSpecialHoursAsync(Guid tenantId, Guid venueId, Guid id, CancellationToken cancellationToken);

    Task<Guid> CreateSpecialHoursAsync(Guid tenantId, Guid venueId, SpecialHoursRequest request, CancellationToken cancellationToken);

    Task UpdateSpecialHoursAsync(Guid tenantId, Guid venueId, Guid id, SpecialHoursRequest request, CancellationToken cancellationToken);

    Task DeleteSpecialHoursAsync(Guid tenantId, Guid venueId, Guid id, CancellationToken cancellationToken);

    /// <summary>Locks the venue row; serialises schedule edits that validate against existing rows.</summary>
    Task LockAsync(Guid tenantId, Guid venueId, CancellationToken cancellationToken);
}

public interface IVenueAccessRepository
{
    Task<bool> IsAssignedAsync(Guid tenantId, Guid tenantUserId, Guid venueId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> ListAssignedAsync(Guid tenantId, Guid tenantUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> ListAllVenueIdsAsync(Guid tenantId, CancellationToken cancellationToken);

    Task ReplaceAssignmentsAsync(Guid tenantId, Guid tenantUserId, IReadOnlyCollection<Guid> venueIds, CancellationToken cancellationToken);
}

/// <summary>
/// Venue-level authorization (D-018): the venue must belong to the current tenant and the caller must be allowed to
/// access it. Inaccessible venues are reported as not found so their existence is not revealed.
/// </summary>
public interface IVenueAccessGuard
{
    Task<VenueRef> EnsureAccessAsync(Guid venueId, CancellationToken cancellationToken);

    /// <summary>Venue ids the caller may access, or null when the caller has access to every venue of the tenant.</summary>
    Task<IReadOnlyList<Guid>?> GetAccessibleVenueIdsAsync(CancellationToken cancellationToken);
}

public sealed record ResourceTypeDto(Guid Id, string Code, string Name, bool IsActive);

public sealed record CreateResourceTypeRequest(string Code, string Name);

public sealed record UpdateResourceTypeRequest(string Name, bool IsActive);

public sealed record ResourceDto(
    Guid Id,
    Guid VenueId,
    Guid? SectionId,
    Guid ResourceTypeId,
    string Name,
    string Code,
    int MinCapacity,
    int Capacity,
    bool IsActive,
    bool IsBookableOnline,
    int SortOrder,
    IReadOnlyDictionary<string, string> Attributes,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CreateResourceRequest(
    Guid VenueId,
    Guid? SectionId,
    Guid ResourceTypeId,
    string Name,
    string Code,
    int MinCapacity,
    int Capacity,
    bool IsBookableOnline = true,
    int SortOrder = 0,
    IReadOnlyDictionary<string, string>? Attributes = null);

public sealed record UpdateResourceRequest(
    Guid? SectionId,
    Guid ResourceTypeId,
    string Name,
    string Code,
    int MinCapacity,
    int Capacity,
    bool IsActive,
    bool IsBookableOnline,
    int SortOrder,
    IReadOnlyDictionary<string, string>? Attributes = null);

public sealed record ResourceQuery(Guid? VenueId, Guid? SectionId, Guid? ResourceTypeId, bool? IsActive, PageRequest Page);

public interface IResourceRepository
{
    Task<IReadOnlyList<ResourceTypeDto>> ListTypesAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<ResourceTypeDto?> GetTypeAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);

    Task<bool> TypeCodeExistsAsync(Guid tenantId, string code, CancellationToken cancellationToken);

    Task<Guid> CreateTypeAsync(Guid tenantId, CreateResourceTypeRequest request, CancellationToken cancellationToken);

    Task UpdateTypeAsync(Guid tenantId, Guid id, UpdateResourceTypeRequest request, CancellationToken cancellationToken);

    Task<PagedResult<ResourceDto>> ListAsync(Guid tenantId, ResourceQuery query, IReadOnlyCollection<Guid>? venueIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<ResourceDto>> ListByVenueAsync(Guid tenantId, Guid venueId, CancellationToken cancellationToken);

    Task<ResourceDto?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);

    Task<bool> CodeExistsAsync(Guid tenantId, Guid venueId, string code, Guid? exceptId, CancellationToken cancellationToken);

    Task<int> CountAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<Guid> CreateAsync(Guid tenantId, CreateResourceRequest request, CancellationToken cancellationToken);

    Task UpdateAsync(Guid tenantId, Guid id, UpdateResourceRequest request, CancellationToken cancellationToken);

    /// <summary>Deletes the resource, or deactivates it when bookings or other rows still reference it.</summary>
    Task<DeleteResult> DeleteOrDeactivateAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
}

public sealed record FloorPlanPositionDto(Guid ResourceId, decimal X, decimal Y, decimal Width, decimal Height, decimal Rotation, string Shape);

public sealed record FloorPlanDto(
    Guid Id,
    Guid VenueId,
    string Name,
    int Width,
    int Height,
    int SortOrder,
    bool IsActive,
    IReadOnlyList<FloorPlanPositionDto> Positions,
    DateTime UpdatedAt);

public sealed record CreateFloorPlanRequest(Guid VenueId, string Name, int Width = 1200, int Height = 800, int SortOrder = 0);

public sealed record UpdateFloorPlanRequest(string Name, int Width, int Height, int SortOrder, bool IsActive);

public sealed record ReplaceFloorPlanPositionsRequest(IReadOnlyList<FloorPlanPositionDto> Positions);

public interface IFloorPlanRepository
{
    Task<IReadOnlyList<FloorPlanDto>> ListAsync(Guid tenantId, Guid venueId, CancellationToken cancellationToken);

    Task<FloorPlanDto?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);

    Task<Guid> CreateAsync(Guid tenantId, CreateFloorPlanRequest request, CancellationToken cancellationToken);

    Task UpdateAsync(Guid tenantId, Guid id, UpdateFloorPlanRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);

    Task ReplacePositionsAsync(Guid tenantId, Guid venueId, Guid id, IReadOnlyList<FloorPlanPositionDto> positions, CancellationToken cancellationToken);
}
