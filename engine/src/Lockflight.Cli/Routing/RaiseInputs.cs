namespace Lockflight.Cli.Routing;

/// <summary>
/// The raises a CI run or a local command passes in: the pull request's branch name (it names a covering feature), its labels
/// (<c>lockflight:class/&lt;class&gt;</c> raises), and the workflow's <c>--raise</c> input. Labels are not a control: anyone with
/// triage rights can add or remove one, so a label can only raise and removing it returns to the computed class.
/// </summary>
internal sealed record RaiseInputs(string? Branch, IReadOnlyList<string> Labels, string? Raise)
{
    public static RaiseInputs None { get; } = new(Branch: null, [], Raise: null);
}
