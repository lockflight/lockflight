using System.Text.Json;
using Lockflight.Cli.Config;

namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// Test projects are locked while any feature has an open red cycle — whatever feature the agent claims to work on, so pointing
/// Spec Kit at another feature does not unlock them. A guardrail: compile-fix edits need a person (<c>lockflight redgreen reset</c>).
/// </summary>
internal static class TestLock
{
    private static readonly string[] ConventionalTestGlobs = ["tests/**", "test/**", "**/*.Tests/**", "**/*.UnitTests/**", "**/*.IntegrationTests/**"];

    public static IReadOnlyList<(string Glob, string Reason)> LockedGlobs(Manifest manifest)
    {
        string features = FeatureLocator.RecordsRoot(manifest.Root);
        if (!Directory.Exists(features))
        {
            return [];
        }

        List<(string, string)> locked = [];
        foreach (string recordPath in Directory.EnumerateFiles(features, RedGreenRecord.FileName, SearchOption.AllDirectories))
        {
            string feature = Path.GetFileName(Path.GetDirectoryName(recordPath)!);
            RedGreenRecord? record;
            try
            {
                record = RedGreenRecord.Load(recordPath);
            }
            catch (Exception exception) when (exception is IOException or JsonException or FormatException or InvalidOperationException)
            {
                // An unreadable record must neither unlock silently nor crash the hook: lock the conventional test folders.
                locked.AddRange(ConventionalGlobs(manifest).Select(glob => (glob, $"an unreadable red/green record of {feature}")));
                continue;
            }

            if (record?.OpenCycle is not RedGreenCycle open)
            {
                continue;
            }

            locked.AddRange(record.TestProjects.Select(project => (project is "." or "" ? "**" : $"{project.TrimEnd('/')}/**", $"red cycle {open.Number} of {feature}")));
        }

        return locked;
    }

    /// <summary>The conventional test folders at the root and inside every unit.</summary>
    private static IEnumerable<string> ConventionalGlobs(Manifest manifest) =>
        ConventionalTestGlobs.Concat(manifest.Units.SelectMany(unit => new[] { $"{unit.Path}/tests/**", $"{unit.Path}/test/**" }));
}
