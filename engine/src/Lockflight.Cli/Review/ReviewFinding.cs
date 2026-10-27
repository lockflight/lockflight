namespace Lockflight.Cli.Review;

/// <summary>
/// One finding of a review run, with the engine's verdict on its reproduction. <see cref="Id"/> identifies it across runs;
/// <see cref="Carried"/> marks an open finding of an earlier run that this run did not raise; <see cref="State"/> is null for findings
/// that are not tracked (minor, nit, rejected).
/// </summary>
internal sealed record ReviewFinding(
    FindingSeverity Severity,
    string File,
    int Line,
    string? Requirement,
    string Title,
    string Evidence,
    string? Reproduction,
    FindingOutcome Outcome)
{
    public string Id { get; init; } = string.Empty;

    public string Dimension { get; init; } = ReviewNames.GeneralDimension;

    public ReproductionResult Reproduced { get; init; } = ReproductionResult.None;

    public bool Carried { get; init; }

    public FindingState? State { get; init; }

    public bool NeedsReproduction => Severity is FindingSeverity.Blocker or FindingSeverity.Major;
}
