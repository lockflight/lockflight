namespace Lockflight.Cli.Routing;

/// <summary>The engine's fixed, ordered list of signal sources. It is not extensible: a new source is added here by an engine change.</summary>
internal static class ChangeSignalSources
{
    public static IReadOnlyList<IChangeSignalSource> All { get; } =
    [
        new SizeSignals(),
        new ProjectSignals(),
        new TestSignals(),
        new CriticalSignals(),
        new StructureSignals(),
    ];
}
