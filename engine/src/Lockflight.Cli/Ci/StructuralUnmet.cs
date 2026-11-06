namespace Lockflight.Cli.Ci;

/// <summary>A condition of a structural-only change this pull request does not meet, and why.</summary>
internal sealed record StructuralUnmet(StructuralCondition Condition, string Why);
