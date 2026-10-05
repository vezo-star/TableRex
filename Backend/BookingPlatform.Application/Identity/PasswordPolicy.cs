namespace BookingPlatform.Application.Identity;

/// <summary>
/// SECURITY.md §1: minimum 12 characters, no composition rules, common passwords rejected.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 12;
    public const int MaxLength = 128;

    // A short list of the most common long passwords; the length rule already excludes most breached passwords.
    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "123456789012", "1234567890123", "12345678901234", "password1234", "password12345", "passwordpassword",
        "qwertyuiop12", "qwertyuiopas", "qwerty123456", "111111111111", "000000000000", "123123123123",
        "abcdefghijkl", "abc123abc123", "iloveyou1234", "letmein12345", "welcome12345", "admin1234567",
        "administrator", "changeme1234", "changemeplease", "football1234", "baseball1234", "monkey123456",
        "sunshine1234", "princess1234", "trustno1trustno1", "superman1234", "1q2w3e4r5t6y", "zaq12wsxcde3",
        "p@ssw0rd1234", "passw0rd1234", "booking12345", "bookingplatform", "restaurant123",
    };

    /// <returns>An error message, or null when the password is acceptable.</returns>
    public static string? Validate(string? password, string? email = null)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
        {
            return $"Password must be at least {MinLength} characters long.";
        }

        if (password.Length > MaxLength)
        {
            return $"Password must be at most {MaxLength} characters long.";
        }

        if (password.Distinct().Count() < 4 || CommonPasswords.Contains(password))
        {
            return "This password is too common. Choose a longer, less predictable password.";
        }

        if (!string.IsNullOrWhiteSpace(email) && password.Contains(email.Split('@')[0], StringComparison.OrdinalIgnoreCase)
            && email.Split('@')[0].Length >= 4)
        {
            return "Password must not contain your email address.";
        }

        return null;
    }
}
