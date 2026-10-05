namespace BookingPlatform.Infrastructure.Configuration;

public sealed class ConnectionStringsOptions
{
    public const string SectionName = "ConnectionStrings";

    public string Postgres { get; set; } = string.Empty;

    public string Redis { get; set; } = string.Empty;
}
