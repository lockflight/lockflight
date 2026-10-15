namespace Lockflight.Cli.Review;

/// <summary>One finding of the review of record, with the engine's verdict on its reproduction.</summary>
internal sealed record ReviewFinding(
    FindingSeverity Severity,
    string File,
    int Line,
    string? Requirement,
    string Title,
    string Evidence,
    string? Reproduction,
    FindingStatus Status)
{
    public bool NeedsReproduction => Severity is FindingSeverity.Blocker or FindingSeverity.Major;
}
