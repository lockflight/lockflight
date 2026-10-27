using System.Globalization;
using System.Text.RegularExpressions;

namespace Lockflight.Cli.Lifecycle;

/// <summary>
/// Task lines of a Spec Kit tasks.md. A task is a test task when it sits under a <c>### Tests for …</c> heading, its description
/// starts with test wording ("Test …", "Write tests …", "Spec for …", "Cover … with tests") or it names a test file
/// (<c>*.spec.ts</c>, <c>*.test.tsx</c>, <c>*Tests.cs</c>, a <c>*.Tests/</c> project) without starting with "Implement" or
/// "Make". Otherwise it is a stub task when it says stub, skeleton, NotImplementedException, or declares or scaffolds signatures;
/// everything else is implementation. Requirement IDs count wherever they appear on the line.
/// </summary>
internal static partial class TasksDocument
{
    private const string NotATest = "no test heading, no test verb, no test file";

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
            string[] requirements = [.. RequirementId().Matches(line).Select(match => match.Value).Distinct()];
            int? story = task.Groups["story"].Success ? int.Parse(task.Groups["story"].Value, CultureInfo.InvariantCulture) : null;
            tasks.Add(new TaskLine(task.Groups["id"].Value, story, requirements, Classify(description, underTestsHeading), task.Groups["done"].Value is "x" or "X", index + 1, description));
        }

        return tasks;
    }

    private static TaskClassification Classify(string description, bool underTestsHeading)
    {
        if (underTestsHeading)
        {
            return new(TaskRole.Test, "test (under a '### Tests' heading)");
        }

        if (TestVerb().IsMatch(description) || TestNoun().IsMatch(description) || CoverWithTests().IsMatch(description))
        {
            return new(TaskRole.Test, "test (starts with test wording)");
        }

        if (StubMarker().Match(description) is { Success: true } stub)
        {
            return new(TaskRole.Stub, $"stub (says '{stub.Value}')");
        }

        if (TestFile().Match(description) is not { Success: true } testFile)
        {
            return new(TaskRole.Implementation, $"implementation ({NotATest}, no stub marker)");
        }

        return ImplementationVerb().IsMatch(description)
            ? new(TaskRole.Implementation, $"implementation (starts with an implementation verb; naming {testFile.Value} does not make it a test)")
            : new(TaskRole.Test, $"test (names the test file {testFile.Value})");
    }

    [GeneratedRegex(@"^\s*-\s+\[(?<done>[ xX])\]\s+(?<id>T\d{3,})\s+(\[P\]\s+)?(\[US(?<story>\d+)\]\s+)?(\((?<refs>[^)]*)\)\s+)?(?<description>.*)$")]
    private static partial Regex Task();

    [GeneratedRegex(@"\bFR-\d{3,}\b")]
    private static partial Regex RequirementId();

    // One leading word may qualify the tests ("Vitest tests …"), but not a verb that runs or satisfies them ("Make tests pass …",
    // "Run tests …"): those tasks write no test.
    [GeneratedRegex(@"^(?!(make|run|rerun|execute|implement|fix|get|keep)\s)(\w+\s+)?(tests?|write tests?|add tests?|contract tests?|integration tests?|unit tests?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TestVerb();

    // "Spec" only as the first word (optionally after "Write"/"Add" or a test framework), so "Specify …" and "Update spec.md"
    // stay implementation.
    [GeneratedRegex(@"^((write|add)\s+)?((vitest|jest|playwright|cypress|e2e|unit|integration|component|contract)\s+)?specs?\b(?![.-])", RegexOptions.IgnoreCase)]
    private static partial Regex TestNoun();

    [GeneratedRegex(@"^cover\b.*\bwith\s+(\w+\s+)?tests?\b", RegexOptions.IgnoreCase)]
    private static partial Regex CoverWithTests();

    // Case-sensitive on purpose: "RoundingTests.cs" and "Shop.Tests/" are test files, "Contest.cs" is not.
    [GeneratedRegex(@"[\w-]+\.(spec|test)\.(ts|tsx|js|jsx|mjs|cjs)\b|\b\w*Tests?\.cs\b|[\w./-]*\.\w*Tests?/")]
    private static partial Regex TestFile();

    [GeneratedRegex(@"^(implement|make)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ImplementationVerb();

    // "stub" also inside a type name ("ShippingStub"), but not "stubborn".
    [GeneratedRegex(@"stub(s|bed)?\b|\bskeletons?\b|\bNotImplementedException\b|\b(scaffold|declare)\b.*\bsignatures?\b", RegexOptions.IgnoreCase)]
    private static partial Regex StubMarker();
}
