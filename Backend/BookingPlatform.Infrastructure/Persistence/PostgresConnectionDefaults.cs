using System.Data.Common;
using Npgsql;

namespace BookingPlatform.Infrastructure.Persistence;

/// <summary>
/// Defaults applied to the application's PostgreSQL connection string unless it sets them explicitly.
/// Kept in step with <c>BookingPlatform.Migrations.PostgresConnectionDefaults</c>.
/// </summary>
public static class PostgresConnectionDefaults
{
    private static readonly string[] GssEncryptionModeKeywords = ["GSS Encryption Mode", "GssEncryptionMode"];

    /// <summary>
    /// Disables GSSAPI encryption negotiation: the platform authenticates with passwords, and Npgsql's default
    /// probe for Kerberos fails noisily in the .NET runtime container images, which do not ship libgssapi_krb5.
    /// </summary>
    public static void Apply(NpgsqlConnectionStringBuilder builder, string configuredConnectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredConnectionString);

        var explicitKeys = new DbConnectionStringBuilder { ConnectionString = configuredConnectionString };
        if (!GssEncryptionModeKeywords.Any(explicitKeys.ContainsKey))
        {
            builder.GssEncryptionMode = GssEncryptionMode.Disable;
        }
    }
}
