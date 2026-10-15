using System.Text.RegularExpressions;

namespace Lockflight.Cli.Adoption;

/// <summary>
/// The path patterns of a shell guard script, as Lockflight globs: its <c>case</c> arms (<c>*docker-compose.override.yml*|*"/Contracts/"*)</c>,
/// or exact names such as <c>".env")</c>) and its <c>[[ "$FILE" == *"name" ]]</c> tests. A file name becomes <c>**/name</c>, a
/// path fragment between slashes becomes <c>**/fragment/**</c>. A pattern without a wildcard counts only when it looks like a
/// path (it has a '.' or a '/'), so arms that match tool names (<c>Edit|Write)</c>) are not mistaken for files. An arm or test
/// that lets the call through (an empty arm, or one with <c>exit 0</c>) is an allow-list entry, not a guarded path: a secrets
/// scanner that allows secrets in <c>.env</c> does not protect <c>.env</c>. Patterns that are not plain (variables, character
/// classes) are left out — the hook still guards them.
/// </summary>
internal static partial class GuardPatterns
{
    public static IReadOnlyList<string> Extract(string script)
    {
        List<string> globs = [];
        IEnumerable<string> armPatterns = CaseArm().Matches(script)
            .Where(arm => Blocks(arm.Groups["body"].Value))
            .SelectMany(arm => arm.Groups["patterns"].Value.Split('|'));
        IEnumerable<string> testPatterns = EqualityTest().Matches(script)
            .Where(test => Blocks(Consequence(script, test.Index + test.Length)))
            .Select(test => test.Groups["pattern"].Value);
        foreach (string pattern in armPatterns.Concat(testPatterns))
        {
            if (ToGlob(pattern.Trim()) is string glob && !globs.Contains(glob, StringComparer.Ordinal))
            {
                globs.Add(glob);
            }
        }

        return globs;
    }

    /// <summary>Whether a branch stops the call: it does something, and that is not letting it through.</summary>
    private static bool Blocks(string body) => body.Trim().Length > 0 && !AllowsThrough().IsMatch(body);

    /// <summary>What runs when a test holds: up to <c>fi</c> for <c>if … then</c>, else the rest of the line (<c>&amp;&amp; exit 0</c>).</summary>
    private static string Consequence(string script, int start)
    {
        int lineEnd = script.IndexOf('\n', start);
        string line = lineEnd < 0 ? script[start..] : script[start..lineEnd];
        if (!line.Contains("then", StringComparison.Ordinal))
        {
            return line;
        }

        int end = FiKeyword().Match(script, start) is { Success: true } fi ? fi.Index : script.Length;
        return script[start..end];
    }

    private static string? ToGlob(string pattern)
    {
        string literal = pattern.Replace("\"", string.Empty, StringComparison.Ordinal).Replace("'", string.Empty, StringComparison.Ordinal).Trim('*');
        if (literal.Length == 0 || literal.IndexOfAny(['$', '[', ']', '?', '*', '(', ')', ' ']) >= 0)
        {
            return null;
        }

        if (!pattern.Contains('*', StringComparison.Ordinal) && literal.IndexOfAny(['.', '/']) < 0)
        {
            return null;
        }

        string trimmed = literal.Trim('/');
        if (trimmed.Length == 0)
        {
            return null;
        }

        return literal.EndsWith('/') || (literal.StartsWith('/') && !trimmed.Contains('.', StringComparison.Ordinal))
            ? $"**/{trimmed}/**"
            : $"**/{trimmed}";
    }

    // A case arm: one or more patterns separated by '|', closed by ')' at the end of the pattern list.
    [GeneratedRegex(@"^\s*(?<patterns>[^()\n#$]+)\)(?<body>(?:(?!;;)[\s\S])*);;", RegexOptions.Multiline)]
    private static partial Regex CaseArm();

    [GeneratedRegex(@"\bexit\s+0\b")]
    private static partial Regex AllowsThrough();

    [GeneratedRegex(@"\bfi\b")]
    private static partial Regex FiKeyword();

    // A [[ … == pattern ]] test (not !=).
    [GeneratedRegex(@"\[\[[^\]\n]*[^!]==\s*(?<pattern>[^\s\]]+)\s*\]\]")]
    private static partial Regex EqualityTest();
}
