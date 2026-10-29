namespace Lockflight.Cli.Ci;

/// <summary>
/// One step of the judge's plan: its id (<c>P0</c>…<c>P5</c>), the commit whose tree it works on (null for a step without a tree),
/// why, what it does there, and how many builds it costs (an upper bound for a conditional step).
/// </summary>
internal sealed record PlanStep(string Id, string? Commit, string Purpose, string Work, int Builds);
