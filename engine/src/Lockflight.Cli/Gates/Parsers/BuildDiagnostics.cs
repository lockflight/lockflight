using System.Globalization;
using System.Text.RegularExpressions;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Gates.Parsers;

/// <summary>Parses MSBuild console diagnostics: <c>path(line,col): warning CODE: message [project]</c>.</summary>
internal static partial class BuildDiagnostics
{
    public static IReadOnlyList<BuildDiagnostic> Parse(string output, string root)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        List<BuildDiagnostic> diagnostics = [];
        foreach (Match match in DiagnosticLine().Matches(output))
        {
            // MSBuild repeats each diagnostic in its summary; the raw line identifies duplicates.
            if (!seen.Add(match.Value.Trim()))
            {
                continue;
            }

            string file = match.Groups["file"].Value.Trim();
            string relative = PathRules.ToRelative(root, file) ?? file.Replace('\\', '/');
            diagnostics.Add(new BuildDiagnostic(
                match.Groups["code"].Value,
                relative,
                int.Parse(match.Groups["line"].Value, CultureInfo.InvariantCulture),
                match.Groups["message"].Value.Trim()));
        }

        return diagnostics;
    }

    [GeneratedRegex(@"^\s*(?<file>[^\r\n(]+)\((?<line>\d+),\d+\): warning (?<code>[A-Za-z]+\d+): (?<message>[^\r\n]*?) \[[^\]\r\n]+\]\s*$", RegexOptions.Multiline)]
    private static partial Regex DiagnosticLine();
}
