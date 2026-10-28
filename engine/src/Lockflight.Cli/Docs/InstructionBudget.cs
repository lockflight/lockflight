using System.Text.RegularExpressions;

namespace Lockflight.Cli.Docs;

/// <summary>
/// Claude Code's documented size targets for instruction files: a <c>CLAUDE.md</c> under 200 lines ("longer files consume more
/// context and reduce adherence"; Claude Code warns at startup above it) and a <c>SKILL.md</c> under 500. A file over its target
/// gets one finding naming its longest sections and where each could go; nothing suggests deleting a rule.
/// </summary>
internal static partial class InstructionBudget
{
    public const int ClaudeMdLines = 200;

    public const int SkillLines = 500;

    private const int SectionsNamed = 3;

    private const int StepsForAProcedure = 3;

    public static DriftFinding? Check(string document, IReadOnlyList<MarkdownLine> lines)
    {
        int? limit = RepoPath.FileName(document) == DocumentSelection.ClaudeMd ? ClaudeMdLines : DocumentSelection.IsSkill(document) ? SkillLines : null;
        int count = lines.Count > 0 && lines[^1].Text.Length == 0 ? lines.Count - 1 : lines.Count;
        if (limit is not int target || count <= target)
        {
            return null;
        }

        string kind = target == ClaudeMdLines ? "a CLAUDE.md" : "a SKILL.md";
        string sections = string.Join("; ", Sections(lines).OrderByDescending(section => section.Lines.Count).Take(SectionsNamed).Select(Describe));
        string advice = sections.Length == 0 ? string.Empty : $". Longest sections: {sections}";
        return new DriftFinding(document, 1, $"{count} lines", $"Claude Code recommends under {target} lines for {kind}{advice}", DriftKind.Budget);
    }

    private static string Describe((string Title, IReadOnlyList<MarkdownLine> Lines) section) =>
        $"'{section.Title}' ({section.Lines.Count} lines): {Destination(section.Lines)}";

    /// <summary>A procedure goes into a skill (loaded when used); rules about one directory into a path-scoped rules file.</summary>
    private static string Destination(IReadOnlyList<MarkdownLine> lines)
    {
        bool procedure = lines.Any(line => line.Kind == MarkdownLineKind.FenceMarker) || lines.Count(line => line.Kind == MarkdownLineKind.Prose && NumberedStepPattern().IsMatch(line.Text)) >= StepsForAProcedure;
        if (procedure)
        {
            return "a skill";
        }

        string[] directories = [.. lines.Where(line => line.Kind == MarkdownLineKind.Prose)
            .SelectMany(line => InlineCode.Spans(line.Text))
            .Where(span => span.Contains('/') && !span.Contains(' '))
            .Select(span => (span.StartsWith("./", StringComparison.Ordinal) ? span[2..] : span).Split('/')[0])
            .Distinct(StringComparer.Ordinal)];
        return directories is [string directory] ? $"a .claude/rules/ file with paths: {directory}/**" : "a skill or a .claude/rules/ file";
    }

    private static IEnumerable<(string Title, IReadOnlyList<MarkdownLine> Lines)> Sections(IReadOnlyList<MarkdownLine> lines)
    {
        string? title = null;
        List<MarkdownLine> current = [];
        foreach (MarkdownLine line in lines)
        {
            int level = line.Kind == MarkdownLineKind.Heading ? MarkdownDocument.HeadingLevel(line.Text) : 0;
            if (level is 1 or 2)
            {
                if (title is not null)
                {
                    yield return (title, current);
                }

                title = level == 2 ? MarkdownDocument.HeadingText(line.Text) : null;
                current = [];
            }

            if (title is not null)
            {
                current.Add(line);
            }
        }

        if (title is not null)
        {
            yield return (title, current);
        }
    }

    [GeneratedRegex(@"^\s*\d+\.\s")]
    private static partial Regex NumberedStepPattern();
}
