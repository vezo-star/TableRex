using System.Text.Json;
using BookingPlatform.Application.Common.Pagination;

namespace BookingPlatform.Application.Bookings;

public static class AllocationModes
{
    public const string Auto = "AUTO";
    public const string Resources = "RESOURCES";
    public const string Combination = "COMBINATION";

    public static bool IsValid(string? mode) => mode is Auto or Resources or Combination;
}

/// <summary>How resources are chosen. Omitted ⇒ <c>AUTO</c> (best fit, as ranked by the availability search).</summary>
public sealed record AllocationRequest(string Mode, IReadOnlyList<Guid>? ResourceIds = null, Guid? CombinationId = null);

/// <summary>Either an existing customer id or new-customer details (an existing customer with the same email is reused).</summary>
public sealed record BookingCustomerInput(Guid? CustomerId, string? FirstName = null, string? LastName = null, string? Email = null, string? Phone = null);

/// <param name="Date">Venue-local business date (an overnight period belongs to the date it opens on).</param>
/// <param name="Time">Venue-local start; after-midnight times belong to the previous business date's overnight period.</param>
public sealed record CreateBookingRequest(
    Guid VenueId,
    BookingCustomerInput? Customer,
    DateOnly Date,
    TimeOnly Time,
    int? DurationMinutes,
    int PartySize,
    AllocationRequest? Allocation = null,
    string? HoldToken = null,
    string? Source = null,
    string? Status = null,
    string? SpecialRequests = null,
    string? InternalNotes = null);

/// <summary>PUT: party size, notes and customer. <c>CustomerId</c> null keeps the current customer.</summary>
public sealed record UpdateBookingRequest(
    int PartySize,
    string? SpecialRequests,
    string? InternalNotes,
    Guid? CustomerId = null,
    int? Version = null);

/// <summary>Move in time and/or to other resources. Omitted values keep the booking's current ones.</summary>
public sealed record MoveBookingRequest(
    DateOnly? Date = null,
    TimeOnly? Time = null,
    int? DurationMinutes = null,
    AllocationRequest? Allocation = null,
    int? Version = null);

public sealed record BookingCommandRequest(int? Version = null);

public sealed record CancelBookingRequest(string? Reason = null, int? Version = null);

public sealed record BookingCustomerDto(Guid Id, string FirstName, string? LastName, string? Email, string? Phone);

public sealed record BookingResourceDto(Guid ResourceId, string Name, string Code, Guid? CombinationId);

public sealed record BookingDto(
    Guid Id,
    Guid VenueId,
    string Reference,
    string Status,
    string Source,
    int PartySize,
    DateTime StartsAt,
    DateTime EndsAt,
    DateOnly BusinessDate,
    TimeOnly Time,
    int DurationMinutes,
    string TimeZone,
    BookingCustomerDto? Customer,
    IReadOnlyList<BookingResourceDto> Resources,
    string? SpecialRequests,
    string? InternalNotes,
    DateTime? ExpiresAt,
    Guid? CreatedByUserId,
    int Version,
    DateTime? ConfirmedAt,
    DateTime? CheckedInAt,
    DateTime? SeatedAt,
    DateTime? CompletedAt,
    DateTime? CancelledAt,
    DateTime? NoShowAt,
    string? CancellationReason,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record BookingListQuery(
    Guid? VenueId,
    DateOnly? Date,
    DateOnly? From,
    DateOnly? To,
    IReadOnlyList<string>? Statuses,
    string? Source,
    Guid? CustomerId,
    Guid? ResourceId,
    Guid? SectionId,
    string? Q,
    PageRequest Page)
{
    /// <summary>Orders by start descending (customer history) instead of ascending (day sheets).</summary>
    public bool NewestFirst { get; init; }
}

public sealed record BookingStatusHistoryDto(Guid Id, string? FromStatus, string ToStatus, string Source, string? Reason, Guid? ChangedByUserId, DateTime ChangedAt);

public sealed record BookingEventDto(Guid Id, string EventType, JsonElement Data, Guid? ActorUserId, DateTime OccurredAt);

public sealed record BookingHistoryDto(Guid BookingId, IReadOnlyList<BookingStatusHistoryDto> StatusHistory, IReadOnlyList<BookingEventDto> Events);

public sealed record CreateHoldRequest(
    Guid VenueId,
    DateOnly Date,
    TimeOnly Time,
    int? DurationMinutes,
    int PartySize,
    AllocationRequest? Allocation = null);

/// <summary><c>Token</c> is returned only by the creating response; replays and reads never include it.</summary>
public sealed record HoldDto(
    Guid Id,
    Guid VenueId,
    string Status,
    int PartySize,
    DateTime StartsAt,
    DateTime EndsAt,
    DateTime ExpiresAt,
    IReadOnlyList<Guid> ResourceIds,
    string? Token);

public static class BlockReasons
{
    public static readonly IReadOnlyList<string> All = ["MAINTENANCE", "PRIVATE_EVENT", "CLEANING", "MANAGEMENT", "UNAVAILABLE", "STAFF_USE"];

    public static bool IsValid(string? reason) => reason is not null && All.Contains(reason);
}

public sealed record CreateBlockRequest(Guid VenueId, IReadOnlyList<Guid> ResourceIds, DateTime StartsAt, DateTime EndsAt, string Reason, string? Notes);

public sealed record BlockDto(
    Guid Id,
    Guid VenueId,
    string Reason,
    string? Notes,
    DateTime StartsAt,
    DateTime EndsAt,
    string Status,
    IReadOnlyList<Guid> ResourceIds,
    Guid? CreatedByUserId,
    DateTime CreatedAt,
    DateTime? CancelledAt);

/// <summary>Booking row plus the venue time zone; mapped to <see cref="BookingDto"/> by the service.</summary>
public sealed record BookingRecord(
    Guid Id,
    Guid VenueId,
    string TimeZone,
    Guid? CustomerId,
    string Reference,
    string Status,
    string Source,
    int PartySize,
    DateTime StartsAt,
    DateTime EndsAt,
    DateOnly BusinessDate,
    string? SpecialRequests,
    string? InternalNotes,
    DateTime? ExpiresAt,
    Guid? CreatedByUserId,
    int Version,
    DateTime? ConfirmedAt,
    DateTime? CheckedInAt,
    DateTime? SeatedAt,
    DateTime? CompletedAt,
    DateTime? CancelledAt,
    DateTime? NoShowAt,
    string? CancellationReason,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    BookingCustomerDto? Customer,
    IReadOnlyList<BookingResourceDto> Resources);

/// <summary>Who creates a booking and under which rules; staff and public creation share <c>BookingService.CreateCoreAsync</c>.</summary>
internal sealed record BookingCreation(
    Guid TenantId,
    Venues.VenueDto Venue,
    BookingTiming Timing,
    CreateBookingRequest Request,
    string Source,
    string Status,
    string HistorySource,
    Guid? UserId,
    string IdempotencyScope)
{
    public Guid BookingId { get; init; } = Guid.CreateVersion7();

    public byte[]? PublicTokenHash { get; init; }

    /// <summary>Restricts AUTO allocation to resources and combinations that are bookable online.</summary>
    public bool OnlineOnly { get; init; }

    public bool MarketingConsent { get; init; }

    /// <summary>Rejects the booking when the customer already has this many upcoming active bookings at the venue.</summary>
    public int? MaxActiveBookingsPerCustomer { get; init; }

    /// <summary>A deposit created with the booking (status HELD until paid; <c>ExpiresAt</c> becomes the booking's expiry).</summary>
    public Payments.DepositRequirement? Deposit { get; init; }
}

public sealed record NewBooking(
    Guid Id,
    Guid VenueId,
    Guid? CustomerId,
    string Reference,
    string Status,
    string Source,
    int PartySize,
    DateTime StartsAt,
    DateTime EndsAt,
    DateOnly BusinessDate,
    string? SpecialRequests,
    string? InternalNotes,
    Guid? CreatedByUserId,
    DateTime Now)
{
    /// <summary>SHA-256 of the guest's manage-booking token (public bookings only).</summary>
    public byte[]? PublicTokenHash { get; init; }

    /// <summary>Required for HELD bookings: when the hold lapses unless confirmed.</summary>
    public DateTime? ExpiresAt { get; init; }
}

public sealed record StatusHistoryEntry(Guid TenantId, Guid VenueId, Guid BookingId, string? FromStatus, string ToStatus, string Source, string? Reason, Guid? UserId);

public sealed record BookingEventEntry(Guid TenantId, Guid VenueId, Guid BookingId, string EventType, object Data, Guid? ActorUserId);

public static class BookingEventTypes
{
    public const string Created = "CREATED";
    public const string Updated = "UPDATED";
    public const string Moved = "MOVED";
    public const string StatusChanged = "STATUS_CHANGED";
    public const string Cancelled = "CANCELLED";
}

public static class HistorySources
{
    public const string Staff = "STAFF";
    public const string Public = "PUBLIC";
    public const string System = "SYSTEM";
}

public interface IBookingRepository
{
    Task<long> NextReferenceSequenceAsync(CancellationToken cancellationToken);

    Task InsertAsync(Guid tenantId, NewBooking booking, CancellationToken cancellationToken);

    Task<BookingRecord?> GetAsync(Guid tenantId, Guid bookingId, CancellationToken cancellationToken);

    /// <summary>Locks the booking row (FOR UPDATE) and returns its venue, status and version.</summary>
    Task<(Guid VenueId, string Status, int Version)?> LockAsync(Guid tenantId, Guid bookingId, CancellationToken cancellationToken);

    Task<PagedResult<BookingRecord>> ListAsync(Guid tenantId, BookingListQuery query, IReadOnlyCollection<Guid>? venueIds, CancellationToken cancellationToken);

    Task ReplaceResourcesAsync(Guid tenantId, Guid venueId, Guid bookingId, IReadOnlyList<Guid> resourceIds, Guid? combinationId, CancellationToken cancellationToken);

    /// <summary>Applies a status change and its timestamp; increments the version.</summary>
    Task UpdateStatusAsync(Guid tenantId, Guid bookingId, string status, string? cancellationReason, DateTime now, CancellationToken cancellationToken);

    Task UpdateDetailsAsync(Guid tenantId, Guid bookingId, int partySize, string? specialRequests, string? internalNotes, Guid? customerId, DateTime now, CancellationToken cancellationToken);

    Task UpdateTimesAsync(Guid tenantId, Guid bookingId, DateTime startsAt, DateTime endsAt, DateOnly businessDate, DateTime now, CancellationToken cancellationToken);

    Task WriteStatusHistoryAsync(StatusHistoryEntry entry, CancellationToken cancellationToken);

    Task WriteEventAsync(BookingEventEntry entry, CancellationToken cancellationToken);

    Task<BookingHistoryDto> GetHistoryAsync(Guid tenantId, Guid bookingId, CancellationToken cancellationToken);

    /// <summary>PENDING/HELD/CONFIRMED bookings of the customer at the venue that start after <paramref name="now"/>.</summary>
    Task<int> CountActiveUpcomingAsync(Guid tenantId, Guid venueId, Guid customerId, DateTime now, CancellationToken cancellationToken);

    /// <summary>The booking a guest token belongs to. The token is the credential, so this lookup is not tenant-scoped.</summary>
    Task<(Guid TenantId, Guid BookingId)?> FindByPublicTokenAsync(byte[] tokenHash, CancellationToken cancellationToken);
}

/// <summary>An active claim to insert. <c>EndsAt</c> already includes turnover for bookings and holds.</summary>
public sealed record AllocationInsert(
    Guid TenantId,
    Guid VenueId,
    string AllocationType,
    Guid? BookingId,
    Guid? HoldId,
    Guid? BlockId,
    IReadOnlyList<Guid> ResourceIds,
    DateTime StartsAt,
    DateTime EndsAt,
    DateTime? ExpiresAt);

public static class AllocationTypes
{
    public const string Booking = "BOOKING";
    public const string Hold = "HOLD";
    public const string Block = "BLOCK";
}

public static class ReleaseReasons
{
    public const string Cancelled = "CANCELLED";
    public const string NoShow = "NO_SHOW";
    public const string Moved = "MOVED";
    public const string HoldExpired = "HOLD_EXPIRED";
    public const string HoldReleased = "HOLD_RELEASED";
    public const string HoldConverted = "HOLD_CONVERTED";
    public const string BlockCancelled = "BLOCK_CANCELLED";
    public const string Expired = "EXPIRED";
}

public interface IAllocationRepository
{
    /// <summary>
    /// Takes transaction-scoped advisory locks on the resources in id order, so concurrent claimants queue instead of
    /// deadlocking on each other's uncommitted rows. The exclusion constraint remains the arbiter.
    /// </summary>
    Task LockResourcesAsync(IReadOnlyCollection<Guid> resourceIds, CancellationToken cancellationToken);

    /// <summary>Lazily releases expired holds on the given resources that overlap the window (DATABASE.md §4.3).</summary>
    Task ReleaseExpiredHoldsAsync(Guid tenantId, IReadOnlyCollection<Guid> resourceIds, DateTime from, DateTime to, DateTime now, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts one allocation per resource (ordered by resource id) inside a savepoint. Returns false — with the
    /// transaction still usable — when the exclusion constraint rejects the claim.
    /// </summary>
    Task<bool> TryInsertAsync(AllocationInsert insert, CancellationToken cancellationToken);

    /// <summary>Resources among <paramref name="resourceIds"/> with an active, unexpired claim overlapping the window.</summary>
    Task<IReadOnlyList<Guid>> FindConflictingResourcesAsync(Guid tenantId, IReadOnlyCollection<Guid> resourceIds, DateTime from, DateTime to, DateTime now, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> ListActiveResourceIdsAsync(Guid tenantId, Guid? bookingId, Guid? holdId, Guid? blockId, CancellationToken cancellationToken);

    Task DeactivateAsync(Guid tenantId, Guid? bookingId, Guid? holdId, Guid? blockId, string reason, DateTime now, CancellationToken cancellationToken);

    /// <summary>Ends a booking's active allocations at <paramref name="endsAt"/> when that is earlier (complete early).</summary>
    Task TruncateBookingAsync(Guid tenantId, Guid bookingId, DateTime endsAt, CancellationToken cancellationToken);
}

public sealed record HoldRecord(
    Guid Id,
    Guid VenueId,
    string OwnerType,
    string OwnerReference,
    string Status,
    int PartySize,
    DateTime StartsAt,
    DateTime EndsAt,
    DateTime ExpiresAt,
    Guid? ConvertedBookingId);

public interface IHoldRepository
{
    Task InsertAsync(Guid tenantId, Guid holdId, Guid venueId, string ownerType, string ownerReference, byte[] tokenHash, int partySize,
        DateTime startsAt, DateTime endsAt, DateTime expiresAt, Guid? userId, CancellationToken cancellationToken);

    Task<HoldRecord?> GetAsync(Guid tenantId, Guid holdId, CancellationToken cancellationToken);

    /// <summary>Finds and locks (FOR UPDATE) a hold by token hash.</summary>
    Task<HoldRecord?> LockByTokenAsync(Guid tenantId, byte[] tokenHash, CancellationToken cancellationToken);

    Task<HoldRecord?> LockAsync(Guid tenantId, Guid holdId, CancellationToken cancellationToken);

    Task SetStatusAsync(Guid tenantId, Guid holdId, string status, Guid? convertedBookingId, DateTime now, CancellationToken cancellationToken);
}

public interface IBlockRepository
{
    Task InsertAsync(Guid tenantId, Guid blockId, CreateBlockRequest request, Guid? userId, CancellationToken cancellationToken);

    Task<BlockDto?> GetAsync(Guid tenantId, Guid blockId, CancellationToken cancellationToken);

    Task<IReadOnlyList<BlockDto>> ListAsync(Guid tenantId, Guid venueId, DateTime? from, DateTime? to, bool includeCancelled, CancellationToken cancellationToken);

    Task<bool> CancelAsync(Guid tenantId, Guid blockId, Guid? userId, DateTime now, CancellationToken cancellationToken);
}

public sealed record NewCustomer(string FirstName, string? LastName, string? Email, string? Phone, string? PhoneNormalized, bool MarketingConsent = false);

public interface IBookingCustomerRepository
{
    Task<BookingCustomerDto?> GetAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken);

    Task<Guid?> FindByEmailAsync(Guid tenantId, string email, CancellationToken cancellationToken);

    Task<Guid> CreateAsync(Guid tenantId, NewCustomer customer, CancellationToken cancellationToken);
}

/// <summary>Resource facts for allocation checks.</summary>
public sealed record AllocatableResource(Guid Id, Guid VenueId, string Name, int MinCapacity, int Capacity, bool IsActive, Guid? SectionId, int SortOrder);

public interface IAllocatableResourceRepository
{
    Task<IReadOnlyList<AllocatableResource>> GetManyAsync(Guid tenantId, IReadOnlyCollection<Guid> resourceIds, CancellationToken cancellationToken);
}
