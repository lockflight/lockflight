using System.Globalization;
using System.Text.RegularExpressions;

namespace Lockflight.Cli.Lifecycle;

/// <summary>
/// The parts of a Spec Kit spec.md that phase checks judge: requirements, user stories, open clarifications and placeholders — Spec
/// Kit template text left in place: bracketed placeholders (<c>[FEATURE NAME]</c>, <c>[DATE]</c>, <c>[###-feature-name]</c>,
/// <c>[Brief Title]</c>, <c>[initial state]</c>, "System MUST [specific capability, e.g., …]") and an unexpanded <c>$ARGUMENTS</c>.
/// </summary>
internal sealed partial record SpecDocument(
    IReadOnlyList<Requirement> Requirements, IReadOnlyList<UserStory> Stories, IReadOnlyList<int> OpenClarificationLines, IReadOnlyList<TemplatePlaceholder> Placeholders)
{

    public IReadOnlyList<string> DuplicateRequirementIds =>
        [.. Requirements.GroupBy(requirement => requirement.Id).Where(group => group.Count() > 1).Select(group => group.Key)];

    public static SpecDocument Parse(string text)
    {
        string[] lines = text.ReplaceLineEndings("\n").Split('\n');
        List<Requirement> requirements = [];
        List<UserStory> stories = [];
        List<int> clarifications = [];
        List<TemplatePlaceholder> placeholders = [];
        (int Number, string Title, int Line, bool Scenarios)? story = null;
        bool inComment = false;

        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            int lineNumber = index + 1;

            // Template guidance lives in HTML comments; it is not part of the spec.
            if (inComment || line.TrimStart().StartsWith("<!--", StringComparison.Ordinal))
            {
                inComment = !line.Contains("-->", StringComparison.Ordinal);
                continue;
            }

            if (line.Contains("[NEEDS CLARIFICATION", StringComparison.Ordinal))
            {
                clarifications.Add(lineNumber);
            }

            if (Placeholder().Match(line) is { Success: true } placeholder)
            {
                placeholders.Add(new TemplatePlaceholder(lineNumber, placeholder.Value));
            }

            if (RequirementLine().Match(line) is { Success: true } requirement)
            {
                requirements.Add(new Requirement(requirement.Groups["id"].Value, lineNumber, requirement.Groups["text"].Value.Trim()));
            }

            if (StoryHeading().Match(line) is { Success: true } heading)
            {
                Close(stories, story);
                story = (int.Parse(heading.Groups["number"].Value, CultureInfo.InvariantCulture), heading.Groups["title"].Value.Trim(), lineNumber, false);
            }
            else if (line.StartsWith("## ", StringComparison.Ordinal) || line.StartsWith("### ", StringComparison.Ordinal))
            {
                Close(stories, story);
                story = null;
            }
            else if (story is { } current && GivenWhenThen().IsMatch(line))
            {
                story = current with { Scenarios = true };
            }
        }

        Close(stories, story);
        return new SpecDocument(requirements, stories, clarifications, placeholders);
    }

    private static void Close(List<UserStory> stories, (int Number, string Title, int Line, bool Scenarios)? story)
    {
        if (story is { } done)
        {
            stories.Add(new UserStory(done.Number, done.Title, done.Line, done.Scenarios));
        }
    }

    [GeneratedRegex(@"^\s*[-*]\s+\*\*(?<id>FR-\d{3,})\*\*\s*:?\s*(?<text>.*)$")]
    private static partial Regex RequirementLine();

    [GeneratedRegex(@"^###\s+User Story\s+(?<number>\d+)\s*[-–—:]\s*(?<title>.+?)(\s*\(Priority:.*\))?\s*$")]
    private static partial Regex StoryHeading();

    /// <summary>
    /// An ALL-CAPS bracket (not a Markdown link, not a clarification marker), a <c>[###-…]</c> branch name, a bracket holding "e.g.",
    /// one of the stock template's bracketed prompts, or an unexpanded <c>$ARGUMENTS</c>.
    /// </summary>
    [GeneratedRegex(@"\[(?!NEEDS CLARIFICATION)[A-Z][A-Z0-9 _/-]{2,}\](?![(\[])|\[###-[^\]]*\]|\[[^\]]*\be\.g\.[^\]]*\]|\[(?:Brief Title|initial state|action|expected outcome|Describe |Explain |specific capability|key interaction|data requirement|behavior|Entity \d|Measurable metric|User satisfaction metric|Business metric|Assumption about|Dependency on|boundary condition|error scenario|Add more user stories|What it represents)[^\]]*\]|\$ARGUMENTS")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"\*\*Given\*\*.*\*\*When\*\*.*\*\*Then\*\*", RegexOptions.IgnoreCase)]
    private static partial Regex GivenWhenThen();
}
