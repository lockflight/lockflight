using Lockflight.Cli.Config;

namespace Lockflight.Cli.Review;

/// <summary>
/// What one review run is bound to besides the reviewer's answer: the feature, the base, HEAD, hashes of the diff and of the prompt,
/// the model and budget it ran with, the manifest's review settings, the Claude Code version, the wall time of the whole run, and why
/// it is not the review of record (empty when it is).
/// </summary>
internal sealed record ReviewRun(
    string Id,
    DateTimeOffset At,
    string Feature,
    ReviewBase Base,
    string Head,
    string DiffSha256,
    string PromptSha256,
    string Model,
    decimal BudgetUsd,
    ReviewSettings Settings,
    string? ClaudeCodeVersion,
    long WallTimeMs,
    IReadOnlyList<string> NotOfRecord)
{
    public bool OfRecord => NotOfRecord.Count == 0;
}
