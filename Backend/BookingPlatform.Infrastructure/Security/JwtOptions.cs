using System.Text;

namespace BookingPlatform.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public const int MinimumKeyBytes = 32;

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    /// <summary>HMAC-SHA256 key; at least 32 bytes (UTF-8). Supplied from the secret store, never from appsettings.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;

    public byte[] GetKeyBytes() => Encoding.UTF8.GetBytes(SigningKey);

    public static bool IsValid(JwtOptions options) =>
        !string.IsNullOrWhiteSpace(options.Issuer)
        && !string.IsNullOrWhiteSpace(options.Audience)
        && Encoding.UTF8.GetByteCount(options.SigningKey ?? string.Empty) >= MinimumKeyBytes
        && !options.SigningKey!.Contains("change-me", StringComparison.OrdinalIgnoreCase)
        && options.AccessTokenMinutes is >= 1 and <= 60;
}
