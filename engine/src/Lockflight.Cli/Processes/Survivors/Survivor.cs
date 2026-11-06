namespace Lockflight.Cli.Processes.Survivors;

/// <summary>A process left running, found in <paramref name="Region"/> once <paramref name="After"/> (program and first argument) had exited.</summary>
internal sealed record Survivor(SweepRegion Region, string After, SweptProcess Process);
