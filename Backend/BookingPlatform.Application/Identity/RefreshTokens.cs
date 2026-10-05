using System.Security.Cryptography;

namespace BookingPlatform.Application.Identity;

/// <summary>Opaque 256-bit refresh tokens; only the SHA-256 hash is ever stored.</summary>
public static class RefreshTokens
{
    public static string Generate() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static byte[] Hash(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        return SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
    }

    /// <summary>Rejects obviously malformed values before touching the database.</summary>
    public static bool IsWellFormed(string? token) =>
        token is { Length: >= 40 and <= 64 } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Random public tokens for unauthenticated links (booking confirmation), stored hashed.</summary>
public static class OpaqueTokens
{
    public static string Generate(int bytes = 24) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Hash(string token) => RefreshTokens.Hash(token);
}
