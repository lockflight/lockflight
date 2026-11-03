using System.Globalization;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// Amends: a person allows one test of the open red cycle to change after its red was accepted. This finds what an amend would
/// unlock, adds it to the record, and describes the amends for status, trace and the review's pull request summary.
/// </summary>
internal static class RedGreenAmends
{
    private const int MaxTestsListed = 5;
    private const int ShortHashLength = 12;
    private const int MinFenceLength = 3;

    /// <summary>
    /// Resolves <paramref name="test"/> (<c>Namespace.Class.Method</c>, <c>Class.Method</c>, the method name or the test key) among the
    /// open cycle's tests and finds the files that declare it.
    /// </summary>
    public static AmendTargetLookup Find(string root, RedGreenRecord? record, string test)
    {
        if (record is null)
        {
            return AmendTargetLookup.Refused("this feature has no red/green evidence, so no test is locked");
        }

        if (record.OpenCycle is not RedGreenCycle open)
        {
            return AmendTargetLookup.Refused("no red cycle is open, so the test projects are not locked: the test can be changed without an amend");
        }

        RedGreenTest[] matches = [.. open.Tests.Where(candidate => Names(candidate, test))];
        if (matches.Length == 0)
        {
            return AmendTargetLookup.Refused(
                $"'{test}' is not a test of red cycle {open.Number}; only a test of the open cycle can be amended. Its tests: {List(open.Tests.Select(candidate => candidate.MethodFilter))}");
        }

        if (matches.Length > 1)
        {
            return AmendTargetLookup.Refused($"'{test}' names {matches.Length} tests of red cycle {open.Number} ({List(matches.Select(match => match.MethodFilter))}); name one as Class.Method");
        }

        RedGreenTest amended = matches[0];
        if (open.OpenAmends.FirstOrDefault(amend => amend.TestKey == amended.Key) is RedGreenAmend waiting)
        {
            return AmendTargetLookup.Refused(
                $"{amended.MethodFilter} is already amended ({waiting.By}, {Date(waiting.At)}); {string.Join(", ", waiting.Files)} stays writable until 'redgreen red' re-checks it");
        }

        IReadOnlyList<string> files = FilesOf(root, record, amended);
        if (files.Count == 0)
        {
            return AmendTargetLookup.Refused(
                $"no C# file under the test projects ({string.Join(", ", record.TestProjects)}) declares {amended.MethodFilter}, so there is no file to unlock");
        }

        string[] sharedWith = [.. open.Tests
            .Where(other => other.Key != amended.Key && FilesOf(root, record, other).Intersect(files, StringComparer.Ordinal).Any())
            .Select(other => other.MethodFilter)];
        return AmendTargetLookup.Found(new AmendTarget(open, amended, files, sharedWith));
    }

    /// <summary>The record with the amend added to the open cycle.</summary>
    public static RedGreenRecord Add(RedGreenRecord record, AmendTarget target, string by, DateTimeOffset at, string reason)
    {
        RedGreenAmend amend = new(target.Test.Key, target.Test.MethodFilter, target.Files, by, at, reason);
        return record with
        {
            Cycles = [.. record.Cycles.Select(cycle => cycle.Number == target.Cycle.Number ? cycle with { Amends = [.. cycle.Amends, amend] } : cycle)],
        };
    }

    /// <summary>What the person is told an amend unlocks: the files, and the other tests of the cycle in them.</summary>
    public static string Unlocks(AmendTarget target)
    {
        string files = string.Join(", ", target.Files);
        string shared = target.SharedWith.Count == 0
            ? string.Empty
            : $" The amend covers the whole file, so these tests of the cycle in it become writable too: {List(target.SharedWith)}.";
        return $"{files} ({(target.Files.Count == 1 ? "the file" : "the files")} declaring {target.Test.MethodFilter}) "
            + $"{(target.Files.Count == 1 ? "becomes" : "become")} writable until 'redgreen red' re-checks the test, which must fail in production code again "
            + $"(RED_STUB or RED_ASSERT).{shared} {Recheck(target)}";
    }

    /// <summary>Where the red after the amend re-checks the test, so nobody breaks the implementation to get it failing again.</summary>
    public static string Recheck(AmendTarget target) => target.Cycle.Snapshot is null
        ? $"Cycle {target.Cycle.Number} has no red-time snapshot, so the re-check runs on the current tree, where an implementation written since the red "
            + $"makes the amended test pass (GREEN_BEFORE). Do not break the implementation to get a red: a person can move it aside for this red, or discard the cycle with '{EngineCommand.Linked} redgreen reset'."
        : $"The re-check runs on the red-time snapshot of cycle {target.Cycle.Number} with the cycle's amended files laid over it, so the implementation written "
            + "since the red cannot make the test pass: do not undo the implementation to get a red. The amended test must compile against the code as it was at red.";

    /// <summary>One line per amend, for status, trace and the pull request summary; empty when there are none.</summary>
    public static IReadOnlyList<string> Lines(RedGreenRecord record) =>
        [.. record.Cycles.SelectMany(cycle => cycle.Amends.Select(amend =>
            $"{TraceMatrix.ShortName(amend.MethodFilter)} in cycle {cycle.Number}, by {amend.By} on {Date(amend.At)}: {amend.Reason} "
            + (amend.RecheckedAt is DateTimeOffset rechecked ? $"(red again on {Date(rechecked)})" : "(waiting for its red)")
            + (amend.Diff is AmendDiff diff ? $"; diff since the red: {diff.Summary} (sha256 {diff.Sha256[..Math.Min(ShortHashLength, diff.Sha256.Length)]})" : string.Empty)))];

    /// <summary>Each recorded amend diff as a collapsed Markdown block, for the pull request summary; empty when there are none.</summary>
    public static IReadOnlyList<string> Diffs(RedGreenRecord record)
    {
        List<string> lines = [];
        foreach (RedGreenAmend amend in record.Amends.Where(amend => amend.Diff is not null))
        {
            string text = amend.Diff!.Text.TrimEnd('\n');
            string fence = new('`', Math.Max(MinFenceLength, LongestBacktickRun(text) + 1));
            lines.AddRange([string.Empty, "<details>", $"<summary>Diff of {TraceMatrix.ShortName(amend.MethodFilter)} since its red</summary>", string.Empty, $"{fence}diff"]);
            lines.AddRange(text.Split('\n'));
            lines.AddRange([fence, string.Empty, "</details>"]);
        }

        return lines;
    }

    private static bool Names(RedGreenTest candidate, string test) =>
        candidate.Key == test
        || candidate.MethodFilter == test
        || candidate.MethodFilter.EndsWith("." + test, StringComparison.Ordinal);

    private static IReadOnlyList<string> FilesOf(string root, RedGreenRecord record, RedGreenTest test)
    {
        int split = test.MethodFilter.LastIndexOf('.');
        return split <= 0 ? [] : TestSourceFiles.Find(root, record.TestProjects, test.MethodFilter[..split], test.MethodFilter[(split + 1)..]);
    }

    private static string List(IEnumerable<string> names)
    {
        string[] all = [.. names];
        string listed = string.Join(", ", all.Take(MaxTestsListed));
        return all.Length > MaxTestsListed ? $"{listed} and {all.Length - MaxTestsListed} more" : listed;
    }

    private static int LongestBacktickRun(string text)
    {
        int longest = 0;
        int current = 0;
        foreach (char character in text)
        {
            current = character == '`' ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }

        return longest;
    }

    private static string Date(DateTimeOffset at) => at.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
}
