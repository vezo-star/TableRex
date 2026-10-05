namespace BookingPlatform.Infrastructure.Persistence;

public static class SqlText
{
    /// <summary>Escapes LIKE/ILIKE wildcards so user input matches literally (PostgreSQL's default escape is backslash).</summary>
    public static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("%", @"\%", StringComparison.Ordinal).Replace("_", @"\_", StringComparison.Ordinal);

    /// <summary>A <c>%term%</c> pattern for <paramref name="value"/>, or null when it is blank.</summary>
    public static string? Contains(string? value) => string.IsNullOrWhiteSpace(value) ? null : $"%{EscapeLike(value.Trim())}%";
}
