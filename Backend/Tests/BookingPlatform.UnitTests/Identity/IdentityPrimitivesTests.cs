using BookingPlatform.Application.Identity;
using BookingPlatform.Infrastructure.Auditing;
using BookingPlatform.Infrastructure.Security;

namespace BookingPlatform.UnitTests.Identity;

public sealed class PasswordPolicyTests
{
    [Theory]
    [InlineData("short")]
    [InlineData("elevenchars")]
    [InlineData("password1234")]
    [InlineData("aaaaaaaaaaaaaaaa")]
    public void Rejects_short_common_or_trivial_passwords(string password) =>
        PasswordPolicy.Validate(password).Should().NotBeNull();

    [Fact]
    public void Rejects_passwords_containing_the_email_local_part() =>
        PasswordPolicy.Validate("my-name-is-jonathan-99", "jonathan@example.test").Should().NotBeNull();

    [Theory]
    [InlineData("correct horse battery staple")]
    [InlineData("Twelve-chars")]
    public void Accepts_long_passphrases_without_composition_rules(string password) =>
        PasswordPolicy.Validate(password, "someone@example.test").Should().BeNull();

    [Fact]
    public void Rejects_passwords_over_the_maximum_length() =>
        PasswordPolicy.Validate(new string('x', 120) + "abcdefghij").Should().NotBeNull();
}

public sealed class RefreshTokenTests
{
    [Fact]
    public void Generated_tokens_are_unique_url_safe_and_well_formed()
    {
        var tokens = Enumerable.Range(0, 200).Select(_ => RefreshTokens.Generate()).ToList();

        tokens.Should().OnlyHaveUniqueItems();
        tokens.Should().AllSatisfy(token =>
        {
            token.Length.Should().Be(43);
            RefreshTokens.IsWellFormed(token).Should().BeTrue();
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("contains spaces contains spaces contains spaces")]
    [InlineData("'; DROP TABLE users; --aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Malformed_tokens_are_rejected_before_hashing(string? token) =>
        RefreshTokens.IsWellFormed(token).Should().BeFalse();

    [Fact]
    public void Hash_is_sha256_and_deterministic()
    {
        var token = RefreshTokens.Generate();

        RefreshTokens.Hash(token).Should().HaveCount(32).And.Equal(RefreshTokens.Hash(token));
    }
}

public sealed class PasswordHasherAdapterTests
{
    private static readonly PasswordHasherAdapter Hasher = new();

    [Fact]
    public void Hash_verifies_and_rejects_wrong_passwords()
    {
        var hash = Hasher.Hash("a long enough passphrase");

        hash.Should().NotContain("passphrase");
        Hasher.Verify(hash, "a long enough passphrase").Should().Be(PasswordVerification.Success);
        Hasher.Verify(hash, "a long enough passphrasE").Should().Be(PasswordVerification.Failed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64-at-all!!")]
    [InlineData("AAAA")]
    public void Corrupt_hashes_fail_closed(string storedHash) =>
        Hasher.Verify(storedHash, "anything").Should().Be(PasswordVerification.Failed);
}

public sealed class JwtOptionsTests
{
    [Theory]
    [InlineData("")]
    [InlineData("too-short-key")]
    [InlineData("CHANGE-ME-CHANGE-ME-CHANGE-ME-CHANGE-ME-1234")]
    public void Weak_or_placeholder_signing_keys_are_rejected(string key) =>
        JwtOptions.IsValid(new JwtOptions { Issuer = "i", Audience = "a", SigningKey = key }).Should().BeFalse();

    [Fact]
    public void Strong_key_is_accepted() =>
        JwtOptions.IsValid(new JwtOptions { Issuer = "i", Audience = "a", SigningKey = new string('k', 32) }).Should().BeTrue();
}

public sealed class AuditRedactionTests
{
    [Fact]
    public void Sensitive_keys_are_redacted_recursively()
    {
        var json = AuditWriter.Serialize(new
        {
            name = "Alice",
            passwordHash = "AQAAAAIAA...",
            nested = new { token = "abc", keep = 1 },
            list = new[] { new { secret = "s" } },
        });

        json.Should().Contain("Alice").And.Contain("\"keep\":1");
        json.Should().NotContainAny("AQAAAAIAA", "\"abc\"", "\"s\"");
        json.Should().Contain("[REDACTED]");
    }

    [Fact]
    public void Null_values_serialize_to_null() => AuditWriter.Serialize(null).Should().BeNull();
}
