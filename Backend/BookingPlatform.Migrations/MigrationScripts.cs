using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace BookingPlatform.Migrations;

public sealed record MigrationScript(string Name, int Version, string Contents);

/// <summary>
/// Loads the SQL scripts embedded from <c>database/migrations</c> and enforces the naming convention
/// <c>V###__description.sql</c> with unique versions, so the applied order is deterministic.
/// </summary>
public static partial class MigrationScripts
{
    internal const string ResourcePrefix = "Migrations.";

    [GeneratedRegex(@"^V(?<version>\d{3})__[a-z0-9_]+\.sql$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    public static IReadOnlyList<MigrationScript> LoadEmbedded() => Load(typeof(MigrationScripts).Assembly);

    public static IReadOnlyList<MigrationScript> Load(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var resourceNames = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .ToArray();

        var fileNames = resourceNames.Select(name => name[ResourcePrefix.Length..]).ToArray();
        var errors = Validate(fileNames);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Invalid migration scripts: " + string.Join("; ", errors));
        }

        return resourceNames
            .Select(resourceName =>
            {
                var fileName = resourceName[ResourcePrefix.Length..];
                using var stream = assembly.GetManifestResourceStream(resourceName)
                    ?? throw new InvalidOperationException($"Embedded migration '{resourceName}' could not be read.");
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return new MigrationScript(fileName, ParseVersion(fileName), reader.ReadToEnd());
            })
            .OrderBy(script => script.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<string> Validate(IEnumerable<string> fileNames)
    {
        ArgumentNullException.ThrowIfNull(fileNames);

        var errors = new List<string>();
        var names = fileNames.ToArray();

        if (names.Length == 0)
        {
            errors.Add("no migration scripts were found");
            return errors;
        }

        foreach (var name in names.Where(name => !NamePattern().IsMatch(name)))
        {
            errors.Add($"'{name}' does not match V###__lower_snake_description.sql");
        }

        var duplicateVersions = names
            .Select(name => NamePattern().Match(name))
            .Where(match => match.Success)
            .GroupBy(match => match.Groups["version"].Value)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);

        foreach (var version in duplicateVersions)
        {
            errors.Add($"version V{version} is used by more than one script");
        }

        return errors;
    }

    private static int ParseVersion(string fileName) =>
        int.Parse(NamePattern().Match(fileName).Groups["version"].Value, System.Globalization.CultureInfo.InvariantCulture);
}
