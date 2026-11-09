using System.Globalization;
using System.Text.RegularExpressions;
using Lockflight.Cli.Config;
using Lockflight.Cli.Gates.Ef;
using Lockflight.Cli.Gates.Steps;

namespace Lockflight.Cli.Init;

/// <summary>
/// What a project with EF Core migrations is told when no tool manifest pins <c>dotnet-ef</c>: under
/// <c>gates.ef.pendingModelChanges: auto</c> (the default) the <c>ef.pendingModelChanges</c> step then reports N/A on every run, so a
/// model change without a migration is not caught. <c>init</c> prints the commands that pin it and <c>doctor</c> warns with them;
/// neither runs them, because the tool version is the team's choice. The pin is looked up as the step does: from the directory the
/// gate runs in (the unit's, or the root's) up to the root. A unit of a workspace shell is left out, as for the patch coverage
/// advice: its CI takes the stricter of its own and the shell's mode, and it runs in the unit's own checkout.
/// </summary>
internal static partial class EfToolAdvice
{
    public const string GuideUrl = "https://github.com/lockflight/lockflight/blob/main/docs/guides/configuration.md#ef-core-model-changes-need-a-migration";

    private const string CentralPackagesFile = "Directory.Packages.props";
    private const string UnknownVersion = "<the EF Core version the project uses>";
    private const int FirstCheckedMajor = 8;

    // The packages whose version is EF Core's own, best first: third-party packages named Microsoft.EntityFrameworkCore.* have theirs.
    private static readonly string[] EfCorePackages =
    [
        "Microsoft.EntityFrameworkCore.Design", "Microsoft.EntityFrameworkCore.Tools", "Microsoft.EntityFrameworkCore.Relational",
        "Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore.SqlServer", "Microsoft.EntityFrameworkCore.Sqlite",
        "Microsoft.EntityFrameworkCore.Cosmos", "Microsoft.EntityFrameworkCore.InMemory",
    ];

    /// <summary>
    /// The contexts the step would check that no tool manifest pins <c>dotnet-ef</c> for, and the manifests that pin it for the
    /// others; <see cref="EfToolState.None"/> when the step does not depend on the pin (no migrations, a mode other than auto, a unit
    /// of a workspace shell) or the tree cannot be read.
    /// </summary>
    public static EfToolState Inspect(Manifest manifest, string root)
    {
        if (manifest.Gates.EfPendingModelChanges != GateMode.Auto || manifest.Shell is not null)
        {
            return EfToolState.None;
        }

        try
        {
            List<EfContext> unpinned = [];
            SortedSet<string> pins = new(StringComparer.Ordinal);
            foreach (string gateDirectory in GateDirectories(manifest, root))
            {
                IReadOnlyList<EfContext> contexts = EfContexts.Find(root, gateDirectory);
                if (contexts.Count == 0)
                {
                    continue;
                }

                if (EfToolPin.Find(root, gateDirectory) is string pin)
                {
                    pins.Add(pin);
                }
                else
                {
                    unpinned.AddRange(contexts);
                }
            }

            return new EfToolState(unpinned, [.. pins]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // doctor and init report; a directory they cannot read is no reason to stop.
            return EfToolState.None;
        }
    }

    /// <summary>The commands that pin <c>dotnet-ef</c> at the root, at the EF Core version the contexts' projects use.</summary>
    public static string Fix(string root, IReadOnlyList<EfContext> contexts)
    {
        string? version = Version(root, contexts);
        string install = $"dotnet tool install {EfToolPin.ToolName} --version {version ?? UnknownVersion}";
        bool hasManifest = EfToolPin.ManifestLocations.Any(location => File.Exists(Path.Combine(root, location)));
        string commands = hasManifest ? install : $"dotnet new tool-manifest, then {install}";
        return Major(version) is int major && major < FirstCheckedMajor
            ? $"the check needs EF Core {FirstCheckedMajor} or later (dotnet ef migrations has-pending-model-changes), and this project uses {version}: after upgrading EF Core, {commands}; until then, gates.ef.pendingModelChanges: off says so"
            : commands;
    }

    /// <summary>What the unpinned contexts lose, for <c>doctor</c> and <c>init</c>.</summary>
    public static string Problem(IReadOnlyList<EfContext> unpinned) =>
        $"{string.Join(", ", unpinned.Select(context => context.Describe()))} {(unpinned.Count == 1 ? "has" : "have")} EF Core migrations, but no tool manifest pins "
        + $"{EfToolPin.ToolName}, so the pr profile's {EfPendingModelChangesStep.StepName} step reports N/A: a model change without a migration is not caught";

    /// <summary>The line <c>init</c> prints for a project with unpinned contexts.</summary>
    public static string InitLine(string root, EfToolState state) =>
        $"EF Core: {Problem(state.Unpinned)}. To check it, from the repository root: {Fix(root, state.Unpinned)}; then commit the tool manifest: {GuideUrl}";

    /// <summary>The directories the gate runs the step in: each unit that may build .NET, or the root without units.</summary>
    private static IEnumerable<string> GateDirectories(Manifest manifest, string root) =>
        manifest.Units.Count == 0
            ? [root]
            : manifest.Units.Where(unit => unit.Kind != UnitKind.None)
                .Select(unit => Path.Combine(root, unit.Path.Replace('/', Path.DirectorySeparatorChar)))
                .Where(Directory.Exists);

    // The version a context's project (or its startup project) states, else the nearest Directory.Packages.props above it.
    private static string? Version(string root, IReadOnlyList<EfContext> contexts)
    {
        foreach (EfContext context in contexts)
        {
            foreach (string project in new[] { context.Project, context.StartupProject }.OfType<string>())
            {
                if (VersionIn(Path.Combine(root, project)) is string stated)
                {
                    return stated;
                }
            }

            for (DirectoryInfo? directory = new(Path.Combine(root, context.Directory)); directory is not null; directory = directory.Parent)
            {
                if (VersionIn(Path.Combine(directory.FullName, CentralPackagesFile)) is string central)
                {
                    return central;
                }

                if (Path.GetRelativePath(root, directory.FullName) == ".")
                {
                    break;
                }
            }
        }

        return null;
    }

    private static string? VersionIn(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        string text = XmlComment().Replace(File.ReadAllText(path), string.Empty);
        Dictionary<string, string> versions = new(StringComparer.OrdinalIgnoreCase);
        foreach (Match element in PackageElement().Matches(text))
        {
            Dictionary<string, string> attributes = new(StringComparer.OrdinalIgnoreCase);
            foreach (Match attribute in PackageAttribute().Matches(element.Groups["attributes"].Value))
            {
                attributes.TryAdd(attribute.Groups["name"].Value, attribute.Groups["value"].Value);
            }

            string? id = attributes.GetValueOrDefault("Include") ?? attributes.GetValueOrDefault("Update");
            string? version = attributes.GetValueOrDefault("VersionOverride") ?? attributes.GetValueOrDefault("Version")
                ?? (ChildVersion().Match(element.Groups["body"].Value) is { Success: true } child ? child.Groups["version"].Value.Trim() : null);
            if (id is not null && version is not null && LiteralVersion().IsMatch(version))
            {
                versions.TryAdd(id, version);
            }
        }

        return EfCorePackages.Select(package => versions.GetValueOrDefault(package)).FirstOrDefault(version => version is not null);
    }

    private static int? Major(string? version) =>
        version is not null && int.TryParse(version.Split('.')[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major) ? major : null;

    [GeneratedRegex(@"<Package(Reference|Version)\b(?<attributes>[^>]*?)(/>|>(?<body>.*?)</Package(Reference|Version)>)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex PackageElement();

    [GeneratedRegex(@"\b(?<name>Include|Update|VersionOverride|Version)\s*=\s*""(?<value>[^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex PackageAttribute();

    [GeneratedRegex(@"<Version>(?<version>[^<]+)</Version>", RegexOptions.IgnoreCase)]
    private static partial Regex ChildVersion();

    [GeneratedRegex(@"^\d+\.\d+\.\d+([-.][\w.-]+)?$")]
    private static partial Regex LiteralVersion();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex XmlComment();
}
