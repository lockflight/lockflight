namespace Lockflight.Cli.Processes.Survivors;

/// <summary>No sweep: the platform has no mechanism the engine uses, setting one up failed, or a test does not want one.</summary>
internal sealed class NoProcessScope(string note) : IProcessScope
{
    public SweepMechanism Mechanism => SweepMechanism.None;

    public string? Note => note;

    public IReadOnlyList<SweptProcess> Sweep() => [];
}
