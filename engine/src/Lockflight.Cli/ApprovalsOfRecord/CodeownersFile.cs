using System.Text.RegularExpressions;

namespace Lockflight.Cli.ApprovalsOfRecord;

/// <summary>
/// A parsed CODEOWNERS file. Each line is a gitignore-style pattern followed by owners (<c>@user</c>, <c>@org/team</c>, an email;
/// any other token names nobody); <c>#</c> starts a comment (<c>\#</c> is a literal one); a line with negation (<c>!</c>) or a
/// character range (<c>[</c>) is invalid and skipped, as GitHub skips it. With sections (GitLab), a header (<c>[Section]</c>,
/// <c>^[Section]</c> for an optional one, optionally with default owners) starts a section: within a section the last matching rule
/// decides, and every required section's decision applies. Without sections the last matching rule of the file decides.
/// </summary>
internal sealed partial class CodeownersFile(string path, IReadOnlyList<CodeownersRule> rules)
{
    public string Path { get; } = path;

    public IReadOnlyList<CodeownersRule> Rules { get; } = rules;

    public static CodeownersFile Parse(string path, string text, bool sections)
    {
        List<CodeownersRule> rules = [];
        string section = string.Empty;
        bool optional = false;
        IReadOnlyList<string> sectionOwners = [];
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            string line = WithoutComment(lines[index]).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            Match header = SectionHeader().Match(line);
            if (sections && header.Success)
            {
                section = header.Groups["name"].Value;
                optional = header.Groups["optional"].Success;
                sectionOwners = Owners(header.Groups["owners"].Value);
                continue;
            }

            string[] fields = Fields().Split(line);
            if (fields[0].StartsWith('!') || fields[0].Contains('[', StringComparison.Ordinal))
            {
                continue;
            }

            string pattern = fields[0].Replace(@"\#", "#", StringComparison.Ordinal).Replace(@"\ ", " ", StringComparison.Ordinal);
            IReadOnlyList<string> owners = Owners(string.Join(' ', fields.Skip(1)));
            rules.Add(new CodeownersRule(pattern, owners.Count == 0 && section.Length > 0 ? sectionOwners : owners, section, optional, index + 1));
        }

        return new CodeownersFile(path, rules);
    }

    /// <summary>
    /// The owners whose approval <paramref name="relativePath"/> requires: per required section the last rule that matches it decides;
    /// empty when none does or it names nobody.
    /// </summary>
    public IReadOnlyList<string> OwnersOf(string relativePath)
    {
        string path = relativePath.Replace('\\', '/').TrimStart('/');
        return [.. Rules.Where(rule => !rule.Optional)
            .GroupBy(rule => rule.Section, StringComparer.OrdinalIgnoreCase)
            .Select(section => section.LastOrDefault(rule => CodeownersPattern.Matches(rule.Pattern, path)))
            .OfType<CodeownersRule>()
            .SelectMany(rule => rule.Owners)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The line without its comment: a <c>#</c> at the start or after whitespace, unless escaped.</summary>
    private static string WithoutComment(string line)
    {
        for (int index = 0; index < line.Length; index++)
        {
            if (line[index] == '#' && (index == 0 || line[index - 1] != '\\') && (index == 0 || char.IsWhiteSpace(line[index - 1])))
            {
                return line[..index];
            }
        }

        return line;
    }

    private static IReadOnlyList<string> Owners(string text) => [.. Fields().Split(text.Trim()).Where(owner => Owner().IsMatch(owner))];

    /// <summary>An owner the platforms accept: <c>@user</c>, <c>@org/team</c>, GitLab's <c>@group/sub</c> and <c>@@role</c>, or an email.</summary>
    [GeneratedRegex(@"^(@@?[A-Za-z0-9][A-Za-z0-9._/-]*|[^@\s]+@[^@\s]+\.[^@\s]+)$")]
    private static partial Regex Owner();

    [GeneratedRegex(@"^(?<optional>\^)?\[(?<name>[^\]]+)\](\[\d+\])?(?<owners>(\s+\S+)*)\s*$")]
    private static partial Regex SectionHeader();

    /// <summary>Whitespace not preceded by a backslash (an escaped space belongs to the pattern).</summary>
    [GeneratedRegex(@"(?<!\\)\s+")]
    private static partial Regex Fields();
}
