namespace Lockflight.Cli.Gates;

/// <summary>
/// One tool run inside a gate (e.g. build, test) and how it was judged. <see cref="Scope"/> says what the step judged (e.g.
/// "3 changed C# files (format)") when that is the run's best answer to "what was checked"; null when the step does not say.
/// </summary>
internal sealed record GateStep(string Name, int ToolExitCode, Verdict Verdict, long DurationMs, string Detail, string? Scope = null)
{
    /// <summary>
    /// The step's stable id (<c>build</c>, <c>coverage.patch</c>) that consumers of the result key on; <see cref="Name"/> is display
    /// text and gains the unit in a workspace run.
    /// </summary>
    public string Id { get; init; } = Name;

    /// <summary>The unit a workspace run judged with this row; null for a row about the whole project.</summary>
    public string? Unit { get; init; }
}
