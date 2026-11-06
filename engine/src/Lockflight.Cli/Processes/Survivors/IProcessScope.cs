namespace Lockflight.Cli.Processes.Survivors;

/// <summary>The platform's way of finding and stopping every process the engine's children started that is still running.</summary>
internal interface IProcessScope
{
    SweepMechanism Mechanism { get; }

    /// <summary>Why there is no mechanism, for the evidence; null when there is one.</summary>
    string? Note { get; }

    /// <summary>
    /// Finds the engine's descendants that are still running, stops them and says what became of each. Called only when the engine
    /// has no child process of its own running, so every one it finds was left behind.
    /// </summary>
    IReadOnlyList<SweptProcess> Sweep();
}
