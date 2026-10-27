using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Lockflight.Cli.Review;

/// <summary>
/// Reads the errors out of <c>dotnet build</c> output (each once, although MSBuild prints them twice) and writes a bounded
/// summary of them for evidence and for the reviewer. File names lose their directories: they are the reproduction's own
/// <c>FindingN.cs</c> (the generated file maps each reproduction to one) or a project file, and absolute paths say nothing useful.
/// </summary>
internal static partial class CompileErrors
{
    public const int MaxErrors = 10;
    public const int MaxMessageCharacters = 300;
    public const int MaxSummaryCharacters = 2_000;

    public static IReadOnlyList<CompileError> Parse(string buildOutput)
    {
        List<CompileError> errors = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (string line in buildOutput.Split('\n'))
        {
            if (Read(line.TrimEnd('\r')) is CompileError error && seen.Add(error.ToString()))
            {
                errors.Add(error);
            }
        }

        return errors;
    }

    /// <summary>The first <see cref="MaxErrors"/> errors, one per line, within <see cref="MaxSummaryCharacters"/>.</summary>
    public static string Summary(IReadOnlyList<CompileError> errors)
    {
        StringBuilder summary = new();
        foreach (CompileError error in errors.Take(MaxErrors))
        {
            string text = error.ToString();
            string line = text.Length > MaxMessageCharacters ? text[..MaxMessageCharacters] + "…" : text;
            if (summary.Length + line.Length + 1 > MaxSummaryCharacters)
            {
                break;
            }

            summary.Append(line).Append('\n');
        }

        if (errors.Count > MaxErrors)
        {
            summary.Append(CultureInfo.InvariantCulture, $"… and {errors.Count - MaxErrors} more\n");
        }

        return summary.ToString().TrimEnd();
    }

    private static CompileError? Read(string line)
    {
        Match located = LocatedError().Match(line);
        if (located.Success)
        {
            string path = located.Groups["path"].Value.Trim();
            return new CompileError(
                path[(path.LastIndexOfAny(['/', '\\']) + 1)..],
                int.Parse(located.Groups["line"].Value, CultureInfo.InvariantCulture),
                int.Parse(located.Groups["column"].Value, CultureInfo.InvariantCulture),
                located.Groups["code"].Value,
                located.Groups["message"].Value.Trim());
        }

        Match general = GeneralError().Match(line);
        return general.Success ? new CompileError(string.Empty, 0, 0, general.Groups["code"].Value, general.Groups["message"].Value.Trim()) : null;
    }

    [GeneratedRegex(@"^\s*(?<path>[^()]+?)\((?<line>\d+),(?<column>\d+)(?:,\d+,\d+)?\)\s*:\s*error\s+(?<code>[A-Za-z]+\d+)\s*:\s*(?<message>.*?)(?:\s+\[[^\]]+\])?\s*$")]
    private static partial Regex LocatedError();

    [GeneratedRegex(@"(?:^|:\s*)error\s+(?<code>[A-Za-z]+\d+)\s*:\s*(?<message>.*?)(?:\s+\[[^\]]+\])?\s*$")]
    private static partial Regex GeneralError();
}
