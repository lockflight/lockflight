namespace Lockflight.Cli.Gates;

/// <summary>The outcome of a gate run over a target directory.</summary>
internal sealed record GateResult(string Target, GateProfile Profile, Verdict Verdict, int? TestsTotal, IReadOnlyList<GateStep> Steps)
{
    /// <summary>
    /// What the run judged, for the <c>Scope:</c> line: the number of tests when the tests ran, else what the last step that says so
    /// checked (e.g. the changed files of the format step), else that nothing was judged.
    /// </summary>
    public string Scope => TestsTotal is int tests
        ? $"{tests} tests"
        : Steps.LastOrDefault(step => step.Scope is not null)?.Scope ?? "nothing judged";

    /// <summary>One line naming each step and its verdict, for hook messages.</summary>
    public string Summary => string.Join("; ", Steps.Select(step =>
        step.Detail.Length > 0
            ? $"{step.Name}={VerdictNames.ToContractName(step.Verdict)} ({step.Detail})"
            : $"{step.Name}={VerdictNames.ToContractName(step.Verdict)}"));
}
