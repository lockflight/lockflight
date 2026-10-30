namespace Lockflight.Cli.Gates.Affected;

/// <summary>How much of a solution the stop profile builds and tests.</summary>
internal enum SelectionKind
{
    /// <summary>Every project, as <c>dotnet build</c> and <c>dotnet test</c> find them: the pr profile, <c>--full</c>, or a fallback.</summary>
    Everything = 0,

    /// <summary>The projects the working tree's changes touch and the projects that depend on them, through a solution filter.</summary>
    Affected = 1,
}
