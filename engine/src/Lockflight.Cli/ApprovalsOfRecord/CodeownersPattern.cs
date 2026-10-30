using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace Lockflight.Cli.ApprovalsOfRecord;

/// <summary>
/// Matches a path against a CODEOWNERS pattern the way GitHub and GitLab read it (gitignore rules without negation or character
/// ranges): a pattern with a <c>/</c> at its start or middle is anchored at the repository root, one without matches at any
/// depth; <c>*</c> and <c>?</c> stay within a directory, <c>**</c> crosses them (<c>/docs/*</c> owns <c>docs/a.md</c>, not
/// <c>docs/x/a.md</c>); a pattern whose last segment is a plain name owns everything under a directory of that name, and a
/// trailing <c>/</c> makes it match directories only. Patterns come from the repository, so matching never backtracks.
/// </summary>
internal static class CodeownersPattern
{
    private static readonly ConcurrentDictionary<string, Regex> Compiled = new(StringComparer.Ordinal);

    public static bool Matches(string pattern, string relativePath) => Compiled.GetOrAdd(pattern, ToRegex).IsMatch(relativePath);

    private static Regex ToRegex(string pattern)
    {
        string trimmed = pattern.TrimEnd('/');
        bool directoryOnly = pattern.EndsWith('/') && trimmed.Length > 0;
        bool anchored = trimmed.StartsWith('/') || trimmed.TrimStart('/').Contains('/', StringComparison.Ordinal);
        string body = Translate(trimmed.TrimStart('/'));
        string prefix = anchored ? "^" : "^(?:.*/)?";
        string lastSegment = trimmed[(trimmed.LastIndexOf('/') + 1)..];
        bool plainName = lastSegment.IndexOfAny(['*', '?']) < 0;
        string suffix = directoryOnly ? "/.*$" : plainName ? "(?:/.*)?$" : "$";
        return new Regex(prefix + body + suffix, RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.NonBacktracking);
    }

    private static string Translate(string pattern)
    {
        StringBuilder regex = new();
        for (int index = 0; index < pattern.Length; index++)
        {
            char current = pattern[index];
            if (current == '*' && index + 1 < pattern.Length && pattern[index + 1] == '*')
            {
                bool directories = index + 2 < pattern.Length && pattern[index + 2] == '/';
                regex.Append(directories ? "(?:.*/)?" : ".*");
                index += directories ? 2 : 1;
            }
            else
            {
                regex.Append(current switch
                {
                    '*' => "[^/]*",
                    '?' => "[^/]",
                    _ => Regex.Escape(current.ToString()),
                });
            }
        }

        return regex.ToString();
    }
}
