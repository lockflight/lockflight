using System.Text.RegularExpressions;
using Lockflight.Cli.Config;

namespace Lockflight.Cli.Routing;

/// <summary>
/// The optional class line of a spec (<c>**Class**: feature</c>), in the header before the first <c>##</c> heading, next to
/// <c>**Status**:</c>; HTML comments are ignored. It can only raise a pull request's class; a value that is not a class name is
/// reported and ignored.
/// </summary>
internal static partial class SpecClassLine
{
    /// <summary>The class the spec asks for, null when it has no class line; <paramref name="invalid"/> holds a value that is no class.</summary>
    public static ChangeClass? Read(string specText, out string? invalid)
    {
        invalid = null;
        string text = HtmlComment().Replace(specText.Replace("\r\n", "\n", StringComparison.Ordinal), string.Empty);
        foreach (string line in text.Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                break;
            }

            if (ClassLine().Match(line) is { Success: true } match)
            {
                string value = match.Groups["value"].Value.Trim().TrimEnd('.').ToLowerInvariant();
                if (ChangeClassNames.TryParse(value, out ChangeClass changeClass))
                {
                    return changeClass;
                }

                invalid = value;
                return null;
            }
        }

        return null;
    }

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex HtmlComment();

    [GeneratedRegex(@"^\s*\*\*Class\*\*\s*:\s*(?<value>.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClassLine();
}
