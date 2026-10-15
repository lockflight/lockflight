using System.Text.RegularExpressions;
using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Lifecycle;

namespace Lockflight.Cli.Testing;

/// <summary>
/// The test workspace of a project for red/green, trace and review: the repository root for a single repository; for a
/// workspace, its .NET units — only those a feature's tasks name with <c>[unit:path]</c> when they name any, so a feature in one
/// service does not build and run all of them.
/// </summary>
internal static partial class TestWorkspaces
{
    public static TestWorkspace For(Manifest manifest, Feature? feature)
    {
        TimeSpan timeLimit = TimeSpan.FromSeconds(manifest.Gates.TimeoutSeconds);
        if (manifest.Units.Count == 0)
        {
            return new TestWorkspace(manifest.Root, [new BuildTarget(manifest.Root, BuildFile: null, manifest.Gates.TestFramework)], timeLimit);
        }

        IReadOnlySet<string> named = feature is null ? new HashSet<string>() : UnitsNamedIn(feature);
        BuildTarget[] targets = [.. manifest.Units
            .Where(unit => WorkspaceGateRunner.BuildsWithDotnet(manifest, unit) && (named.Count == 0 || named.Contains(unit.Path)))
            .Select(unit => new BuildTarget(Path.Combine(manifest.Root, unit.Path.Replace('/', Path.DirectorySeparatorChar)), unit.Build, unit.TestFramework ?? manifest.Gates.TestFramework))];
        return new TestWorkspace(manifest.Root, targets, timeLimit);
    }

    /// <summary>The units a feature's <c>tasks.md</c> names with <c>[unit:path]</c>.</summary>
    public static IReadOnlySet<string> UnitsNamedIn(Feature feature) => File.Exists(feature.TasksPath)
        ? UnitTag().Matches(File.ReadAllText(feature.TasksPath)).Select(match => match.Groups["path"].Value.Trim().TrimEnd('/')).ToHashSet(StringComparer.OrdinalIgnoreCase)
        : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"\[unit:(?<path>[^\]]+)\]")]
    private static partial Regex UnitTag();
}
