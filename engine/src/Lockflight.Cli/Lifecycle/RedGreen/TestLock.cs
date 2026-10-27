using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// Test projects are locked while any feature has an open red cycle — whatever feature the agent claims to work on, so pointing
/// Spec Kit at another feature does not unlock them. A guardrail: a test that must change needs a person, who either amends it
/// (<c>lockflight redgreen amend</c>, which makes the file that declares it writable until the next red) or discards the evidence
/// (<c>lockflight redgreen reset</c>).
/// </summary>
internal static class TestLock
{
    private static readonly string[] ConventionalTestGlobs = ["tests/**", "test/**", "**/*.Tests/**", "**/*.UnitTests/**", "**/*.IntegrationTests/**"];

    public static IReadOnlyList<(string Glob, string Reason)> LockedGlobs(Manifest manifest) =>
        [.. Locks(manifest).SelectMany(featureLock => featureLock.Globs.Select(glob => (glob, featureLock.Reason)))];

    /// <summary>
    /// Test files a person's open amend makes writable, relative to the root and '/'-separated: an exception to the lock of the
    /// amending feature only, so a file that another feature's open cycle also locks stays locked.
    /// </summary>
    public static IReadOnlySet<string> AmendedFiles(Manifest manifest)
    {
        FeatureLock[] locks = [.. Locks(manifest)];
        HashSet<string> writable = new(StringComparer.Ordinal);
        foreach (FeatureLock featureLock in locks)
        {
            writable.UnionWith(featureLock.AmendedFiles.Where(file => !locks.Any(other =>
                other.Feature != featureLock.Feature && other.Globs.Any(glob => PathRules.Matches(glob, file)))));
        }

        return writable;
    }

    private static IEnumerable<FeatureLock> Locks(Manifest manifest)
    {
        string features = FeatureLocator.RecordsRoot(manifest.Root);
        if (!Directory.Exists(features))
        {
            yield break;
        }

        foreach (string recordPath in Directory.EnumerateFiles(features, RedGreenRecord.FileName, SearchOption.AllDirectories))
        {
            string feature = Path.GetFileName(Path.GetDirectoryName(recordPath)!);
            if (Read(recordPath) is not RecordReading reading)
            {
                // An unreadable record must neither unlock silently nor crash the hook: lock the conventional test folders.
                yield return new FeatureLock(recordPath, [.. ConventionalGlobs(manifest)], $"an unreadable red/green record of {feature}", []);
                continue;
            }

            if (reading.Record?.OpenCycle is not RedGreenCycle open)
            {
                continue;
            }

            string[] globs = [.. reading.Record.TestProjects.Select(project => project is "." or "" ? "**" : $"{project.TrimEnd('/')}/**")];
            yield return new FeatureLock(recordPath, globs, $"red cycle {open.Number} of {feature}", [.. open.OpenAmends.SelectMany(amend => amend.Files)]);
        }
    }

    /// <summary>What reading the record found (its <see cref="RecordReading.Record"/> is null when there is none); null when it cannot be read.</summary>
    private static RecordReading? Read(string recordPath)
    {
        try
        {
            return new RecordReading(RedGreenRecord.Load(recordPath));
        }
        catch (Exception exception) when (exception is IOException or JsonException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The conventional test folders at the root and inside every unit.</summary>
    private static IEnumerable<string> ConventionalGlobs(Manifest manifest) =>
        ConventionalTestGlobs.Concat(manifest.Units.SelectMany(unit => new[] { $"{unit.Path}/tests/**", $"{unit.Path}/test/**" }));

    /// <summary>One feature's lock: its record (identity), the globs it locks, why, and the files its open amends make writable.</summary>
    private sealed record FeatureLock(string Feature, IReadOnlyList<string> Globs, string Reason, IReadOnlyList<string> AmendedFiles);

    private sealed record RecordReading(RedGreenRecord? Record);
}
