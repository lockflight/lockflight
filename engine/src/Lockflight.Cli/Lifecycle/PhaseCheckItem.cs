namespace Lockflight.Cli.Lifecycle;

/// <summary>One deterministic check of a lifecycle phase and what to fix when it fails.</summary>
internal sealed record PhaseCheckItem(string Name, bool Passed, string Detail);
