using System.Reflection;
using System.Text.RegularExpressions;

namespace Release.Api;

public sealed record ReleaseMetadata(string Service, string Version, string Revision)
{
    private static readonly Regex SafeValue = new(
        @"\A[A-Za-z0-9][A-Za-z0-9._-]{0,63}\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static ReleaseMetadata FromEntryAssembly()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(ReleaseMetadata).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+', 2)[0];
        var revision = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "BuildRevision")
            ?.Value;

        return new ReleaseMetadata(
            "Release.Api",
            Normalize(version, "0.0.0"),
            Normalize(revision, "local"));
    }

    public static string Normalize(string? value, string fallback) =>
        value is not null && SafeValue.IsMatch(value) ? value : fallback;
}

public sealed record HealthResponse(string Status);
