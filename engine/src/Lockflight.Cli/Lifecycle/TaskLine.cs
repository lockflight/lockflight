namespace Lockflight.Cli.Lifecycle;

/// <summary>A task in tasks.md: <c>- [ ] T012 [P] [US1] (FR-001, FR-003) Description</c>.</summary>
/// <param name="Requirements">Every distinct requirement ID on the line, in order of appearance, wherever it is written.</param>
internal sealed record TaskLine(string Id, int? Story, IReadOnlyList<string> Requirements, TaskClassification Classification, bool IsDone, int Line, string Description)
{
    public TaskRole Role => Classification.Role;

    public string Reason => Classification.Reason;
}
