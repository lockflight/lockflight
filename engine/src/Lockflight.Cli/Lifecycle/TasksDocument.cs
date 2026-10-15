using System.Globalization;
using System.Text.RegularExpressions;

namespace Lockflight.Cli.Lifecycle;

/// <summary>
/// Task lines of a Spec Kit tasks.md. A task is a test task when it sits under a <c>### Tests for …</c> heading or its description
/// starts with a test verb ("Test …", "Write tests …", "Add test …").
/// </summary>
internal static partial class TasksDocument
{
    public static IReadOnlyList<TaskLine> Parse(string text)
    {
        string[] lines = text.ReplaceLineEndings("\n").Split('\n');
        List<TaskLine> tasks = [];
        bool underTestsHeading = false;
        bool inComment = false;

        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            if (inComment || line.TrimStart().StartsWith("<!--", StringComparison.Ordinal))
            {
                inComment = !line.Contains("-->", StringComparison.Ordinal);
                continue;
            }

            if (line.StartsWith('#'))
            {
                underTestsHeading = line.StartsWith("### Tests", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (Task().Match(line) is not { Success: true } task)
            {
                continue;
            }

            string description = task.Groups["description"].Value.Trim();
            string[] requirements = [.. RequirementId().Matches(task.Groups["refs"].Value).Select(match => match.Value).Distinct()];
            int? story = task.Groups["story"].Success ? int.Parse(task.Groups["story"].Value, CultureInfo.InvariantCulture) : null;
            bool isTest = underTestsHeading || TestVerb().IsMatch(description);
            tasks.Add(new TaskLine(task.Groups["id"].Value, story, requirements, isTest, task.Groups["done"].Value is "x" or "X", index + 1, description));
        }

        return tasks;
    }

    [GeneratedRegex(@"^\s*-\s+\[(?<done>[ xX])\]\s+(?<id>T\d{3,})\s+(\[P\]\s+)?(\[US(?<story>\d+)\]\s+)?(\((?<refs>[^)]*)\)\s+)?(?<description>.*)$")]
    private static partial Regex Task();

    [GeneratedRegex(@"FR-\d{3,}")]
    private static partial Regex RequirementId();

    [GeneratedRegex(@"^(\w+\s+)?(tests?|write tests?|add tests?|contract tests?|integration tests?|unit tests?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TestVerb();
}
