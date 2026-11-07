namespace Lockflight.Cli.Processes.Survivors;

/// <summary>What a sweep found left running.</summary>
internal enum SurvivorKind
{
    /// <summary>A process the engine's children started.</summary>
    Process = 0,

    /// <summary>A Docker container created while the judge ran.</summary>
    Container = 1,
}
