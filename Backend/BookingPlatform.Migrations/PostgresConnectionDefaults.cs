using System.Data.Common;
using Npgsql;

namespace BookingPlatform.Migrations;

/// <summary>
/// Defaults applied to the migrator's PostgreSQL connection string unless it sets them explicitly.
/// Kept in step with <c>BookingPlatform.Infrastructure.Persistence.PostgresConnectionDefaults</c>.
/// </summary>
public static class PostgresConnectionDefaults
{
    private static readonly string[] GssEncryptionModeKeywords = ["GSS Encryption Mode", "GssEncryptionMode"];

    /// <summary>
    /// Disables GSSAPI encryption negotiation: the platform authenticates with passwords, and Npgsql's default
    /// probe for Kerberos fails noisily in the .NET runtime container images, which do not ship libgssapi_krb5.
    /// </summary>
    public static string Apply(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var explicitKeys = new DbConnectionStringBuilder { ConnectionString = connectionString };
        if (GssEncryptionModeKeywords.Any(explicitKeys.ContainsKey))
        {
            return connectionString;
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString) { GssEncryptionMode = GssEncryptionMode.Disable };
        return builder.ConnectionString;
    }
}
