using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BookingPlatform.Migrations;

public sealed record AppRoleOptions(string UserName, string Password);

/// <summary>
/// Ensures the least-privilege login role the Api and Worker connect with (D-095): data access only, no DDL, never a
/// superuser and never BYPASSRLS. Runs after migrations on every deploy, so grants cover new tables and the password
/// follows APP_DB_PASSWORD (rotation = change the variable and redeploy).
/// </summary>
public sealed partial class AppRoleProvisioner(ILogger<AppRoleProvisioner> logger)
{
    private const int MinPasswordLength = 16;
    private const int MaxPasswordLength = 128;
    private const int ScramIterations = 4096;

    /// <returns>An error message, or null when the options are acceptable.</returns>
    public static string? Validate(AppRoleOptions options)
    {
        if (!RoleName().IsMatch(options.UserName))
        {
            return "APP_DB_USER must be 1-63 characters: lower-case letters, digits and underscores, starting with a letter.";
        }

        // Printable ASCII only: SCRAM requires SASLprep normalisation for other characters, which is not implemented.
        var password = options.Password;
        if (password.Length is < MinPasswordLength or > MaxPasswordLength || password.Any(c => c is < '!' or > '~'))
        {
            return $"APP_DB_PASSWORD must be {MinPasswordLength}-{MaxPasswordLength} printable ASCII characters without spaces.";
        }

        // The Api and Worker receive it inside an unquoted connection string, where these would end or quote the value.
        if (password.IndexOfAny([';', '\'', '"']) >= 0)
        {
            return "APP_DB_PASSWORD must not contain ';' or quotes; it is embedded in connection strings.";
        }

        if (password.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
        {
            return "APP_DB_PASSWORD still contains the CHANGE_ME placeholder.";
        }

        return password.Distinct().Count() < 8 ? "APP_DB_PASSWORD is too predictable." : null;
    }

    public async Task RunAsync(string connectionString, AppRoleOptions options, CancellationToken cancellationToken)
    {
        await using var dataSource = NpgsqlDataSource.Create(PostgresConnectionDefaults.Apply(connectionString));
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        await using (var self = new NpgsqlCommand("SELECT current_user", connection))
        {
            if (string.Equals((string?)await self.ExecuteScalarAsync(cancellationToken), options.UserName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("APP_DB_USER must differ from the migration (owner) user.");
            }
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        bool exists;
        await using (var find = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = @name)", connection, transaction))
        {
            find.Parameters.AddWithValue("name", options.UserName);
            exists = (bool)(await find.ExecuteScalarAsync(cancellationToken))!;
        }

        // Role DDL cannot take bind parameters. The name is validated above; the password is sent only as a SCRAM
        // verifier, so the plain text never reaches the server or its statement log.
        var role = $"\"{options.UserName}\"";
        var verifier = ScramVerifier(options.Password).Replace("'", "''", StringComparison.Ordinal);
        var attributes = "LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS NOINHERIT";
        string[] statements =
        [
            exists
                ? $"ALTER ROLE {role} WITH {attributes} PASSWORD '{verifier}'"
                : $"CREATE ROLE {role} WITH {attributes} PASSWORD '{verifier}'",
            $"GRANT CONNECT ON DATABASE \"{connection.Database.Replace("\"", "\"\"", StringComparison.Ordinal)}\" TO {role}",
            $"GRANT USAGE ON SCHEMA public TO {role}",
            $"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {role}",
            $"GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO {role}",
            $"REVOKE ALL ON TABLE public.{MigrationRunner.JournalTable} FROM {role}",
            $"GRANT SELECT ON TABLE public.{MigrationRunner.JournalTable} TO {role}",
        ];
        foreach (var sql in statements)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Application database role {Role} {Outcome}", options.UserName, exists ? "updated" : "created");
    }

    /// <summary>A PostgreSQL SCRAM-SHA-256 password verifier (RFC 5802 / RFC 7677), as stored in pg_authid.</summary>
    private static string ScramVerifier(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var salted = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, ScramIterations, HashAlgorithmName.SHA256, 32);
        var clientKey = HMACSHA256.HashData(salted, "Client Key"u8);
        var storedKey = SHA256.HashData(clientKey);
        var serverKey = HMACSHA256.HashData(salted, "Server Key"u8);
        return $"SCRAM-SHA-256${ScramIterations}:{Convert.ToBase64String(salt)}${Convert.ToBase64String(storedKey)}:{Convert.ToBase64String(serverKey)}";
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{0,62}$")]
    private static partial Regex RoleName();
}
