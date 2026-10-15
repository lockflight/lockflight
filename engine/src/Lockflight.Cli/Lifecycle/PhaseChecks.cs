using Lockflight.Cli.Lifecycle.Approvals;

namespace Lockflight.Cli.Lifecycle;

/// <summary>
/// Deterministic checks at the end of the specify and tasks phases. Tasks that belong to no user story (setup,
/// foundational, polish) need no requirement; story tasks must cite known requirements, and within a story the test tasks come
/// before the implementation tasks (stub declarations may come anywhere). A failure names each task, its line and why it was
/// classified as it was, because the agent fixing tasks.md cannot see the classification rules.
/// </summary>
internal static class PhaseChecks
{
    private const int MaxListed = 5;

    private const string HowToMark = "Mark a test task with a '### Tests for …' heading above it, test wording at the start "
        + "('Write tests …', 'Spec for …', 'Cover … with tests') or the test file it writes (*.spec.ts, *.test.ts, *Tests.cs, a *.Tests/ "
        + "project); mark a stub declaration with 'stub', 'skeleton', 'declare signatures' or 'NotImplementedException' so it may "
        + "come first; move implementation tasks after the story's last test task";

    public static IReadOnlyList<PhaseCheckItem> Specify(Feature feature)
    {
        if (!File.Exists(feature.SpecPath))
        {
            return [new PhaseCheckItem("spec.exists", false, $"{feature.Id}/spec.md is missing")];
        }

        SpecDocument spec = SpecDocument.Parse(File.ReadAllText(feature.SpecPath));
        UserStory[] withoutScenarios = [.. spec.Stories.Where(story => !story.HasAcceptanceScenarios)];
        return
        [
            new("spec.requirements", spec.Requirements.Count > 0, spec.Requirements.Count > 0
                ? $"{spec.Requirements.Count} requirements"
                : "no requirements; list them as '- **FR-001**: System MUST …' under ## Requirements"),
            new("spec.unique-ids", spec.DuplicateRequirementIds.Count == 0, spec.DuplicateRequirementIds.Count == 0
                ? "requirement IDs are unique"
                : $"duplicate IDs: {string.Join(", ", spec.DuplicateRequirementIds)}"),
            new("spec.clarified", spec.OpenClarificationLines.Count == 0, spec.OpenClarificationLines.Count == 0
                ? "no open [NEEDS CLARIFICATION]"
                : $"{spec.OpenClarificationLines.Count} open [NEEDS CLARIFICATION] (lines {Lines(spec.OpenClarificationLines)}); resolve them with the user (/speckit-clarify)"),
            new("spec.no-placeholders", spec.Placeholders.Count == 0, spec.Placeholders.Count == 0
                ? "no Spec Kit template placeholders left"
                : $"{spec.Placeholders.Count} template placeholders left ({string.Join(", ", spec.Placeholders.Take(MaxListed).Select(placeholder => $"line {placeholder.Line}: {placeholder.Text}"))}); replace them with this feature's content or delete the example lines"),
            new("spec.stories", spec.Stories.Count > 0 && withoutScenarios.Length == 0, spec.Stories.Count == 0
                ? "no '### User Story N - Title' sections"
                : withoutScenarios.Length == 0
                    ? $"{spec.Stories.Count} user stories with Given/When/Then scenarios"
                    : $"no Given/When/Then scenario in: {string.Join(", ", withoutScenarios.Take(MaxListed).Select(story => $"User Story {story.Number}"))}"),
        ];
    }

    /// <param name="units">The workspace's unit paths: <c>[unit:path]</c> tags must name one of them; empty for a single repository.</param>
    public static IReadOnlyList<PhaseCheckItem> Tasks(Feature feature, ApprovalCheck specApproval, IReadOnlyList<string> units)
    {
        List<PhaseCheckItem> items = [new("spec.approved", specApproval.State == ApprovalState.Valid, specApproval.Describe())];
        if (!File.Exists(feature.TasksPath) || !File.Exists(feature.SpecPath))
        {
            items.Add(new PhaseCheckItem("tasks.exists", false, $"{feature.Id}/tasks.md (and spec.md) must exist; run /speckit-tasks"));
            return items;
        }

        HashSet<string> known = [.. SpecDocument.Parse(File.ReadAllText(feature.SpecPath)).Requirements.Select(requirement => requirement.Id)];
        IReadOnlyList<TaskLine> tasks = TasksDocument.Parse(File.ReadAllText(feature.TasksPath));
        TaskLine[] storyTasks = [.. tasks.Where(task => task.Story is not null)];
        HashSet<string> cited = [.. tasks.SelectMany(task => task.Requirements)];

        string[] uncovered = [.. known.Where(id => !cited.Contains(id)).Order(StringComparer.Ordinal)];
        items.Add(new("tasks.cover-requirements", uncovered.Length == 0, uncovered.Length == 0
            ? $"all {known.Count} requirements have tasks"
            : $"requirements without a task: {string.Join(", ", uncovered.Take(MaxListed))}"));

        TaskLine[] uncited = [.. storyTasks.Where(task => task.Requirements.Count == 0)];
        string[] unknown = [.. cited.Where(id => !known.Contains(id)).Order(StringComparer.Ordinal)];
        items.Add(new("tasks.cite-requirements", uncited.Length == 0 && unknown.Length == 0, (uncited.Length, unknown.Length) switch
        {
            (0, 0) => "every story task cites known requirements",
            (_, 0) => $"story tasks that cite no FR-xxx: {Listed([.. uncited.Select(task => $"{task.Id} line {task.Line}")])}",
            _ => $"tasks cite requirements the spec does not define: {string.Join(", ", unknown.Take(MaxListed))}",
        }));

        string[] outOfOrder = [.. storyTasks.GroupBy(task => task.Story).SelectMany(OutOfOrder)];
        items.Add(new("tasks.tests-first", outOfOrder.Length == 0, outOfOrder.Length == 0
            ? "in every story the tests come before the implementation"
            : $"{Listed(outOfOrder)}. {HowToMark}"));

        string[] unknownUnits = [.. Testing.TestWorkspaces.UnitsNamedIn(feature).Where(unit => !units.Contains(unit, StringComparer.OrdinalIgnoreCase)).Order(StringComparer.Ordinal)];
        if (unknownUnits.Length > 0 || units.Count > 0)
        {
            items.Add(new("tasks.units", unknownUnits.Length == 0, unknownUnits.Length == 0
                ? "every [unit:…] tag names a unit (red/green builds only the named units, or all .NET units when none is named)"
                : $"[unit:…] tags that name no unit in lockflight.yaml: {string.Join(", ", unknownUnits.Take(MaxListed))}"));
        }

        return items;
    }

    /// <summary>The story's implementation tasks that come before its last test task, or the story itself when it has no test task.</summary>
    private static IEnumerable<string> OutOfOrder(IGrouping<int?, TaskLine> story)
    {
        TaskLine[] work = [.. story.Where(task => task.Role != TaskRole.Stub).OrderBy(task => task.Line)];
        TaskLine? lastTest = work.LastOrDefault(task => task.Role == TaskRole.Test);
        if (lastTest is null)
        {
            return [$"US{story.Key} has no test task"];
        }

        return work
            .Where(task => task.Role == TaskRole.Implementation && task.Line < lastTest.Line)
            .Select(task => $"{task.Id} line {task.Line}: {task.Reason} comes before test task {lastTest.Id} (line {lastTest.Line}) of US{story.Key}");
    }

    private static string Listed(string[] entries) => entries.Length > MaxListed
        ? $"{string.Join("; ", entries.Take(MaxListed))}; and {entries.Length - MaxListed} more"
        : string.Join("; ", entries);

    private static string Lines(IReadOnlyList<int> lines) => string.Join(", ", lines.Take(MaxListed));
}
