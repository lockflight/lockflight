using System.Text.RegularExpressions;

namespace Lockflight.Cli.Gates.Parsers;

/// <summary>
/// Names of failed tests from Microsoft.Testing.Platform's console output (<c>failed Namespace.Class.Method (12ms)</c>), so a red
/// gate tells the agent what to look at instead of only that something failed.
/// </summary>
internal static partial class FailedTests
{
    public const int MaxListed = 5;

    public static IReadOnlyList<string> Find(string output) =>
        [.. FailedLine().Matches(output).Select(match => match.Groups["name"].Value.Trim()).Distinct(StringComparer.Ordinal)];

    /// <summary>Failure lines, each theory row and repeated name counted, to compare with the runner's own count of failures.</summary>
    public static int CountFailures(string output) => FailedLine().Count(output);

    /// <summary>"3 failed: A, B, C" — at most <see cref="MaxListed"/> names, then a count of the rest.</summary>
    public static string Describe(IReadOnlyList<string> names) => Describe(names, "failed");

    /// <summary>"3 &lt;what happened&gt;: A, B, C" — at most <see cref="MaxListed"/> names, then a count of the rest.</summary>
    public static string Describe(IReadOnlyList<string> names, string happened)
    {
        IEnumerable<string> listed = names.Take(MaxListed);
        string more = names.Count > MaxListed ? $" and {names.Count - MaxListed} more" : string.Empty;
        return $"{names.Count} {happened}: {string.Join(", ", listed)}{more}";
    }

    // MTP's result line exactly: at the start of the line (assertion messages and stack traces below it are indented), the word
    // "failed", the name, and a duration such as (12ms) or (17s 814ms).
    [GeneratedRegex(@"^failed (?<name>[^\r\n]+?) \((?:\d+(?:ms|s|m|h) ?)+\)\r?$", RegexOptions.Multiline)]
    private static partial Regex FailedLine();
}
