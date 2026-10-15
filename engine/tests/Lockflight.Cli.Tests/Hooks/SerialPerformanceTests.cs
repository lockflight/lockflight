namespace Lockflight.Cli.Tests.Hooks;

/// <summary>Timing tests run alone, so other tests' file-system load does not count against them.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SerialPerformanceTests
{
    public const string Name = "serial-performance";
}
