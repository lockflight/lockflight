namespace Lockflight.Cli.Gates.Steps;

/// <summary>
/// The test step's verdict plus what the runner reported: how many tests ran and which failed. A VSTest run also says where it
/// wrote its results, where the coverage step finds the Cobertura reports.
/// </summary>
internal sealed record TestStepOutcome(GateStep Step, int? TestsTotal, IReadOnlyList<string> FailedTests)
{
    public string? ResultsDirectory { get; init; }
}
