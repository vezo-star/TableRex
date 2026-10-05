using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace BookingPlatform.Migrations;

public sealed record PlatformAdminOptions(string Email, string Password, string DisplayName);

public enum PlatformAdminOutcome
{
    Created,
    Promoted,
    AlreadyAdmin,
}

/// <summary>
/// Creates the first platform admin of a new environment (D-092). Allowed in Production, idempotent, and never changes
/// the password of an existing account: an existing user is only promoted.
/// </summary>
public sealed class PlatformAdminBootstrapper(ILogger<PlatformAdminBootstrapper> logger)
{
    // Must match BookingPlatform.Infrastructure.Security.PasswordHasherAdapter and Application PasswordPolicy.
    private const int HashIterations = 210_000;
    private const int MinPasswordLength = 12;
    private const int MaxPasswordLength = 128;

    /// <returns>An error message, or null when the options are acceptable.</returns>
    public static string? Validate(PlatformAdminOptions options)
    {
        var email = options.Email;
        var at = email.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0 || at != email.LastIndexOf('@') || at == email.Length - 1 || email.Length > 254 || email.Any(char.IsWhiteSpace))
        {
            return "PLATFORM_ADMIN_EMAIL is not a valid email address.";
        }

        if (string.IsNullOrWhiteSpace(options.DisplayName) || options.DisplayName.Length > 100)
        {
            return "PLATFORM_ADMIN_NAME must be 1-100 characters.";
        }

        var password = options.Password;
        if (password.Length is < MinPasswordLength or > MaxPasswordLength)
        {
            return $"PLATFORM_ADMIN_PASSWORD must be {MinPasswordLength}-{MaxPasswordLength} characters.";
        }

        var localPart = email[..at];
        if (password.Distinct().Count() < 4 || (localPart.Length >= 4 && password.Contains(localPart, StringComparison.OrdinalIgnoreCase)))
        {
            return "PLATFORM_ADMIN_PASSWORD is too predictable (it must not contain the email name and needs variety).";
        }

        return null;
    }

    public async Task<PlatformAdminOutcome> RunAsync(string connectionString, PlatformAdminOptions options, CancellationToken cancellationToken)
    {
        await using var dataSource = NpgsqlDataSource.Create(PostgresConnectionDefaults.Apply(connectionString));
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var find = new NpgsqlCommand(
            "SELECT id, is_platform_admin FROM users WHERE email = lower(@email)::citext FOR UPDATE", connection, transaction);
        find.Parameters.AddWithValue("email", options.Email);
        Guid? userId = null;
        var isAdmin = false;
        await using (var reader = await find.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                userId = reader.GetGuid(0);
                isAdmin = reader.GetBoolean(1);
            }
        }

        PlatformAdminOutcome outcome;
        if (userId is null)
        {
            var hasher = new PasswordHasher<object>(Options.Create(new PasswordHasherOptions
            {
                CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
                IterationCount = HashIterations,
            }));
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO users (email, password_hash, display_name, is_platform_admin)
                VALUES (lower(@email)::citext, @hash, @name, true)
                """,
                connection,
                transaction);
            insert.Parameters.AddWithValue("email", options.Email);
            insert.Parameters.AddWithValue("hash", hasher.HashPassword(new object(), options.Password));
            insert.Parameters.AddWithValue("name", options.DisplayName.Trim());
            await insert.ExecuteNonQueryAsync(cancellationToken);
            outcome = PlatformAdminOutcome.Created;
        }
        else if (!isAdmin)
        {
            await using var promote = new NpgsqlCommand(
                "UPDATE users SET is_platform_admin = true, updated_at = now() WHERE id = @id", connection, transaction);
            promote.Parameters.AddWithValue("id", userId.Value);
            await promote.ExecuteNonQueryAsync(cancellationToken);
            outcome = PlatformAdminOutcome.Promoted;
        }
        else
        {
            outcome = PlatformAdminOutcome.AlreadyAdmin;
        }

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Platform admin bootstrap: {Outcome}", outcome);
        return outcome;
    }
}
