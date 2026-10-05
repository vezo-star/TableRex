using System.Globalization;
using NodaTime;

namespace BookingPlatform.Domain.Bookings;

public static class BookingStatus
{
    public const string Pending = "PENDING";
    public const string Held = "HELD";
    public const string Confirmed = "CONFIRMED";
    public const string CheckedIn = "CHECKED_IN";
    public const string Seated = "SEATED";
    public const string Completed = "COMPLETED";
    public const string Cancelled = "CANCELLED";
    public const string NoShow = "NO_SHOW";

    public static readonly IReadOnlyList<string> All = [Pending, Held, Confirmed, CheckedIn, Seated, Completed, Cancelled, NoShow];

    /// <summary>Statuses whose booking occupies its resources.</summary>
    public static readonly IReadOnlyList<string> Active = [Pending, Held, Confirmed, CheckedIn, Seated];

    public static bool IsValid(string? status) => status is not null && All.Contains(status);

    public static bool IsActive(string status) => Active.Contains(status);

    public static bool IsTerminal(string status) => status is Completed or Cancelled or NoShow;
}

public static class BookingSource
{
    public const string Staff = "STAFF";
    public const string Phone = "PHONE";
    public const string WalkIn = "WALK_IN";
    public const string Public = "PUBLIC";
    public const string Api = "API";

    /// <summary>Sources staff may choose when creating a booking.</summary>
    public static readonly IReadOnlyList<string> StaffSelectable = [Staff, Phone, WalkIn];

    public static bool IsValid(string? source) => source is Staff or Phone or WalkIn or Public or Api;
}

/// <summary>Explicit booking commands; the API never sets a status directly.</summary>
public enum BookingCommand
{
    Confirm,
    CheckIn,
    Seat,
    Complete,
    NoShow,
    Cancel,
    Expire,
}

/// <summary>The booking status machine (DOMAIN_MODEL §4), used by every command.</summary>
public static class BookingStatusMachine
{
    private static readonly Dictionary<BookingCommand, (string[] From, string To)> Transitions = new()
    {
        [BookingCommand.Confirm] = ([BookingStatus.Pending, BookingStatus.Held], BookingStatus.Confirmed),
        [BookingCommand.CheckIn] = ([BookingStatus.Confirmed], BookingStatus.CheckedIn),
        [BookingCommand.Seat] = ([BookingStatus.Confirmed, BookingStatus.CheckedIn], BookingStatus.Seated),
        [BookingCommand.Complete] = ([BookingStatus.Seated], BookingStatus.Completed),
        [BookingCommand.NoShow] = ([BookingStatus.Confirmed], BookingStatus.NoShow),
        [BookingCommand.Cancel] = ([BookingStatus.Pending, BookingStatus.Held, BookingStatus.Confirmed, BookingStatus.CheckedIn], BookingStatus.Cancelled),
        [BookingCommand.Expire] = ([BookingStatus.Held], BookingStatus.Cancelled),
    };

    /// <summary>Statuses a new booking may start in. Walk-ins start SEATED.</summary>
    public static readonly IReadOnlyList<string> InitialStatuses =
        [BookingStatus.Pending, BookingStatus.Held, BookingStatus.Confirmed, BookingStatus.Seated];

    public static bool CanStartAs(string status) => InitialStatuses.Contains(status);

    public static bool CanApply(string currentStatus, BookingCommand command) =>
        Transitions.TryGetValue(command, out var transition) && transition.From.Contains(currentStatus);

    /// <summary>The status after applying <paramref name="command"/>, or null when the transition is not allowed.</summary>
    public static string? TryApply(string currentStatus, BookingCommand command) =>
        CanApply(currentStatus, command) ? Transitions[command].To : null;

    public static bool CanTransition(string from, string to) =>
        Transitions.Values.Any(t => t.To == to && t.From.Contains(from));

    /// <summary>Bookings that may still be moved or edited (they occupy resources).</summary>
    public static bool IsMutable(string status) => BookingStatus.IsActive(status);
}

public static class BookingReference
{
    /// <summary><c>BK-{yyyyMMdd business date}-{sequence padded to 6}</c>.</summary>
    public static string Format(LocalDate businessDate, long sequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        return string.Create(CultureInfo.InvariantCulture, $"BK-{businessDate:yyyyMMdd}-{sequence:D6}");
    }
}
