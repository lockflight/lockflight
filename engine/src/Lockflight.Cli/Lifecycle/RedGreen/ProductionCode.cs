namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// The repository's production code as red/green recognises it: the directories of its non-test projects, their assembly names,
/// and the types those assemblies declare (full names with nested types joined by '.', as stack frames print them).
/// </summary>
internal sealed record ProductionCode(IReadOnlyList<string> ProjectDirectories, IReadOnlySet<string> AssemblyNames, IReadOnlySet<string> TypeNames)
{
    public static ProductionCode None { get; } = new([], new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));
}
