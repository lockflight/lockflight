namespace Lockflight.Cli.Routing;

/// <summary>One requirement of a change class: its kind, level and, for a measured kind, the threshold (null when none).</summary>
internal sealed record Requirement(RequirementKind Kind, RequirementLevel Level, int? Threshold);
