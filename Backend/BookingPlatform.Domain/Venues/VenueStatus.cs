namespace BookingPlatform.Domain.Venues;

public static class VenueStatus
{
    public const string Active = "ACTIVE";
    public const string Inactive = "INACTIVE";

    public static bool IsValid(string? status) => status is Active or Inactive;
}

public static class FloorPlanShapes
{
    public const string Rect = "RECT";
    public const string Round = "ROUND";

    public static bool IsValid(string? shape) => shape is Rect or Round;
}
