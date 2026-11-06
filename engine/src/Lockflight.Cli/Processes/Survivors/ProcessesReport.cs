namespace Lockflight.Cli.Processes.Survivors;

/// <summary>
/// What the sweeps of one CI judge run found: the mechanism (and why none), how many sweeps ran, the processes left running (at most
/// <see cref="ProcessSweep.MaxSurvivors"/>, <paramref name="More"/> counting the rest) and the commands whose output was held open.
/// </summary>
internal sealed record ProcessesReport(
    SweepMechanism Mechanism, string? Note, int Sweeps, IReadOnlyList<Survivor> Survivors, int More, IReadOnlyList<string> OutputHeld);
