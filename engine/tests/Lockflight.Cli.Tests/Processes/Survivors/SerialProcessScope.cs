namespace Lockflight.Cli.Tests.Processes.Survivors;

/// <summary>
/// Tests that make the test host itself sweep run alone: a sweep stops every process the host started, and other tests' processes
/// would be among them.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SerialProcessScope
{
    public const string Name = "serial-process-scope";
}
