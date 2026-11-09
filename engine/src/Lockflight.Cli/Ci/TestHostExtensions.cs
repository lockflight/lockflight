using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// What a test run loads besides the test assemblies, as host-tampering findings: the data collectors, in-process collectors,
/// adapter paths and loggers each app's <c>.runsettings</c> names (<see cref="RunSettingsExtensions"/>); the MSBuild properties
/// through which <c>dotnet test</c> adds them (<see cref="TestApps.VSTestProperties"/>); and test adapter, logger and collector
/// assemblies in a test app's output folder (VSTest loads <c>*.TestAdapter.dll</c>, <c>*.TestLogger.dll</c> and <c>*Collector.dll</c>
/// from there by their names). The well-known adapters and collectors of the supported frameworks are left out unless a project of the repository
/// builds an assembly of that name. Each finding's id leaves out the app, so a new test project that uses what the base's projects
/// already use brings nothing new. Microsoft.Testing.Platform extensions, which register in the test app's own entry point, are not
/// listed.
/// </summary>
internal static class TestHostExtensions
{
    /// <summary>The kind of an MSBuild property finding.</summary>
    public const string PropertyKind = "vstest-property";

    /// <summary>The kind of an extension assembly found in a test app's output folder.</summary>
    public const string AssemblyKind = "test-extension-assembly";

    private const string RunSettingsSource = "runsettings";
    private const string MSBuildSource = "msbuild";
    private const string OutputSource = "test-output";
    private const string OutputLocation = "test output";

    /// <summary>The file names VSTest looks for extensions under, in the test source's folder.</summary>
    private static readonly string[] ExtensionSuffixes = [".TestAdapter.dll", ".TestLogger.dll", "Collector.dll"];

    private static readonly string[] KnownExtensions =
    [
        "xunit.runner.visualstudio.testadapter.dll", "xunit.runner.visualstudio.dotnetcore.testadapter.dll", "NUnit3.TestAdapter.dll",
        "Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.dll", "MSTest.TestAdapter.dll", "coverlet.collector.dll",
        "Microsoft.TestPlatform.TestHostRuntimeProvider.dll", "Microsoft.VisualStudio.TestPlatform.Extensions.Trx.TestLogger.dll",
        "Microsoft.VisualStudio.TestPlatform.Extensions.Html.TestLogger.dll", "Microsoft.TestPlatform.Extensions.BlameDataCollector.dll",
        "Microsoft.VisualStudio.TraceDataCollector.dll", "Microsoft.CodeCoverage.Core.dll",
    ];

    public static IReadOnlyList<TamperingFinding> Find(IReadOnlyList<TestApp> apps, string root, IReadOnlySet<string> repositoryProjects)
    {
        List<TamperingFinding> findings = [];
        foreach (TestApp app in apps)
        {
            if (app.RunSettingsFile is string settings)
            {
                string location = Path.GetRelativePath(root, settings).Replace('\\', '/');
                findings.AddRange(RunSettingsExtensions.Read(settings).Select(entry => new TamperingFinding(RunSettingsSource, entry.Kind, location, entry.Identity)));
            }

            findings.AddRange(app.VSTestProperties.Select(property => new TamperingFinding(MSBuildSource, PropertyKind, property.Key, property.Value)));
            findings.AddRange(ExtensionAssemblies(app.OutputDirectory, repositoryProjects)
                .Select(name => new TamperingFinding(OutputSource, AssemblyKind, OutputLocation, name)));
        }

        return [.. findings.DistinctBy(finding => finding.Id)];
    }

    private static IEnumerable<string> ExtensionAssemblies(string output, IReadOnlySet<string> repositoryProjects) =>
        !Directory.Exists(output)
            ? []
            : Directory.EnumerateFiles(output, "*.dll", SearchOption.TopDirectoryOnly)
                .Select(path => Path.GetFileName(path))
                .Where(IsExtension)
                .Where(name => repositoryProjects.Contains(Path.GetFileNameWithoutExtension(name)) || !KnownExtensions.Contains(name, StringComparer.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal);

    private static bool IsExtension(string name) => ExtensionSuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
}
