namespace Lockflight.Cli.Ci;

/// <summary>
/// What one <c>lockflight ci</c> run judges, validated: the repository root, the head commit, the target branch's tip
/// (<see cref="Base"/>; null only for a push without a previous commit in the clone), the source branch name (used only to find the
/// feature), the platform, the event, where evidence goes, an optional read-only shell clone, and whether a review job is expected.
/// </summary>
internal sealed record JudgeInputs(
    string Root,
    string Head,
    string? Base,
    string? Branch,
    JudgePlatform Platform,
    JudgeEvent Event,
    string EvidenceDirectory,
    string? ShellPath,
    bool ReviewExpected);
