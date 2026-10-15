namespace Lockflight.Cli.Config;

/// <summary>What the gates do with a unit of a workspace.</summary>
internal enum UnitKind
{
    /// <summary>Decided from the unit's files: .NET when it holds a solution or project file, otherwise none.</summary>
    Auto = 0,

    /// <summary>A .NET unit: built, tested and formatted by the gates.</summary>
    Dotnet = 1,

    /// <summary>Guarded but not built: a stack Lockflight has no adapter for yet, or a repository without code (gitops).</summary>
    None = 2,
}
