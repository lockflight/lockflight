namespace Lockflight.Cli.Review;

/// <summary>
/// One finding of a review run, with the engine's verdict on its reproduction. <see cref="Id"/> identifies it across runs;
/// <see cref="Carried"/> marks an open finding of an earlier run that this run did not raise; <see cref="State"/> is null for findings
/// that are not tracked (minor, nit, rejected). <see cref="EarlierReproductions"/> are the reproductions earlier runs gave an open
/// finding that differ from <see cref="Reproduction"/>: each is re-run too, and the finding is fixed only when all of them pass. <see cref="ReproductionAttempts"/> counts the builds of this run's reproduction (two when it
/// was repaired after it did not compile), and <see cref="CompileErrors"/> holds a bounded summary of the errors of each attempt that did not compile.
/// <see cref="SpecQuote"/> is the requirement sentence the reviewer says the finding encodes, and <see cref="SpecAnchor"/> whether the
/// engine found it in the spec (recorded only; it does not change the outcome).
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

    public IReadOnlyList<string> EarlierReproductions { get; init; } = [];

    public int ReproductionAttempts { get; init; }

    public IReadOnlyList<string> CompileErrors { get; init; } = [];

    public FindingState? State { get; init; }

    public string? SpecQuote { get; init; }

    public SpecAnchor SpecAnchor { get; init; } = SpecAnchor.None;

    public bool NeedsReproduction => Severity is FindingSeverity.Blocker or FindingSeverity.Major;
}
