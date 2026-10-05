using DbUp;
using DbUp.Engine;
using DbUp.Engine.Output;
using Microsoft.Extensions.Logging;

namespace BookingPlatform.Migrations;

public sealed record MigrationResult(bool Successful, IReadOnlyList<string> AppliedScripts, Exception? Error);

/// <summary>
/// Applies embedded migrations in name order, one transaction per script, journaled in <c>public.schema_versions</c>.
/// Already-applied scripts are skipped, so running it repeatedly is safe.
/// </summary>
public sealed class MigrationRunner(ILogger<MigrationRunner> logger)
{
    public const string JournalSchema = "public";
    public const string JournalTable = "schema_versions";

    public MigrationResult Run(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var scripts = MigrationScripts.LoadEmbedded()
            .Select(script => new SqlScript(script.Name, script.Contents))
            .ToArray();

        var engine = DeployChanges.To
            .PostgresqlDatabase(PostgresConnectionDefaults.Apply(connectionString))
            .WithScripts(scripts)
            .JournalToPostgresqlTable(JournalSchema, JournalTable)
            .WithTransactionPerScript()
            .LogTo(new UpgradeLogAdapter(logger))
            .Build();

        var result = engine.PerformUpgrade();
        var applied = result.Scripts.Select(script => script.Name).ToArray();

        if (result.Successful)
        {
            logger.LogInformation("Migrations completed. Newly applied scripts: {AppliedCount}", applied.Length);
        }
        else
        {
            logger.LogError(result.Error, "Migration failed at script {ScriptName}", result.ErrorScript?.Name);
        }

        return new MigrationResult(result.Successful, applied, result.Error);
    }

    private sealed class UpgradeLogAdapter(ILogger logger) : IUpgradeLog
    {
#pragma warning disable CA2254 // DbUp supplies its own message templates.
        public void LogTrace(string format, params object[] args) => logger.LogTrace(format, args);

        public void LogDebug(string format, params object[] args) => logger.LogDebug(format, args);

        public void LogInformation(string format, params object[] args) => logger.LogInformation(format, args);

        public void LogWarning(string format, params object[] args) => logger.LogWarning(format, args);

        public void LogError(string format, params object[] args) => logger.LogError(format, args);

        public void LogError(Exception ex, string format, params object[] args) => logger.LogError(ex, format, args);
#pragma warning restore CA2254
    }
}
