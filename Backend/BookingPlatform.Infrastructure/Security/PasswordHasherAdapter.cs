using BookingPlatform.Application.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using IdentityResult = Microsoft.AspNetCore.Identity.PasswordVerificationResult;

namespace BookingPlatform.Infrastructure.Security;

/// <summary>PBKDF2-HMAC-SHA512 via ASP.NET Core Identity's hasher, used standalone (SECURITY.md §1).</summary>
public sealed class PasswordHasherAdapter : IPasswordHasher
{
    public const int IterationCount = 210_000;

    private static readonly object User = new();
    private readonly PasswordHasher<object> _hasher;
    private readonly string _dummyHash;

    public PasswordHasherAdapter()
    {
        _hasher = new PasswordHasher<object>(Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
            IterationCount = IterationCount,
        }));
        _dummyHash = _hasher.HashPassword(User, Guid.NewGuid().ToString("N"));
    }

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        return _hasher.HashPassword(User, password);
    }

    public PasswordVerification Verify(string hashedPassword, string providedPassword)
    {
        if (string.IsNullOrEmpty(hashedPassword) || string.IsNullOrEmpty(providedPassword))
        {
            return PasswordVerification.Failed;
        }

        try
        {
            return _hasher.VerifyHashedPassword(User, hashedPassword, providedPassword) switch
            {
                IdentityResult.Success => PasswordVerification.Success,
                IdentityResult.SuccessRehashNeeded => PasswordVerification.SuccessRehashNeeded,
                _ => PasswordVerification.Failed,
            };
        }
        catch (FormatException)
        {
            return PasswordVerification.Failed;
        }
    }

    public void VerifyDummy(string providedPassword) =>
        _hasher.VerifyHashedPassword(User, _dummyHash, string.IsNullOrEmpty(providedPassword) ? "x" : providedPassword);
}
