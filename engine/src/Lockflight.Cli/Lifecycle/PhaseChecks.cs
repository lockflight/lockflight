using Lockflight.Cli.Lifecycle.Approvals;

namespace Lockflight.Cli.Lifecycle;

/// <summary>
/// Deterministic checks at the end of the specify and tasks phases. Tasks that belong to no user story (setup,
/// foundational, polish) need no requirement; story tasks must cite known requirements, and within a story the test tasks come
/// before the implementation tasks (stub declarations may come first).
/// </summary>
internal static class PhaseChecks
{
    private const int MaxListed = 5;

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
            (_, 0) => $"story tasks without (FR-xxx): {string.Join(", ", uncited.Take(MaxListed).Select(task => task.Id))}",
            _ => $"tasks cite requirements the spec does not define: {string.Join(", ", unknown.Take(MaxListed))}",
        }));

        string[] misordered = [.. storyTasks
            .GroupBy(task => task.Story)
            .Where(story => !TestsFirst(story))
            .Select(story => $"US{story.Key}")];
        items.Add(new("tasks.tests-first", misordered.Length == 0, misordered.Length == 0
            ? "in every story the tests come before the implementation"
            : $"implementation before tests (or no test task) in: {string.Join(", ", misordered)}"));

        string[] unknownUnits = [.. Testing.TestWorkspaces.UnitsNamedIn(feature).Where(unit => !units.Contains(unit, StringComparer.OrdinalIgnoreCase)).Order(StringComparer.Ordinal)];
        if (unknownUnits.Length > 0 || units.Count > 0)
        {
            items.Add(new("tasks.units", unknownUnits.Length == 0, unknownUnits.Length == 0
                ? "every [unit:…] tag names a unit (red/green builds only the named units, or all .NET units when none is named)"
                : $"[unit:…] tags that name no unit in lockflight.yaml: {string.Join(", ", unknownUnits.Take(MaxListed))}"));
        }

        return items;
    }

    private static bool TestsFirst(IEnumerable<TaskLine> story)
    {
        TaskLine[] work = [.. story.Where(task => !IsStub(task)).OrderBy(task => task.Line)];
        int firstImplementation = Array.FindIndex(work, task => !task.IsTest);
        int lastTest = Array.FindLastIndex(work, task => task.IsTest);
        return lastTest >= 0 && (firstImplementation < 0 || lastTest < firstImplementation);
    }

    private static bool IsStub(TaskLine task) => task.Description.Contains("stub", StringComparison.OrdinalIgnoreCase);

    private static string Lines(IReadOnlyList<int> lines) => string.Join(", ", lines.Take(MaxListed));
}
