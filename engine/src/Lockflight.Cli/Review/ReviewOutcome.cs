namespace Lockflight.Cli.Review;

/// <summary>The reviewer's parsed answer plus what the run cost; <see cref="Error"/> is set when no usable review came back.</summary>
internal sealed record ReviewOutcome(string Verdict, string Summary, IReadOnlyList<ReviewFinding> Findings, decimal CostUsd, long DurationMs, string? Error);
