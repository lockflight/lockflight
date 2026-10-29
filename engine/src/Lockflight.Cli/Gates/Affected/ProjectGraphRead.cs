namespace Lockflight.Cli.Gates.Affected;

/// <summary>
/// The project graph of a solution, or why it could not be read with certainty (a reference through an MSBuild property, a
/// <c>Directory.Build.props</c> that adds references, a project in another repository): the stop profile then runs everything.
/// </summary>
/// <param name="InheritedTestHints">
/// A <c>Directory.Build.props</c> or <c>.targets</c> above a project names a test framework, so a project may be a test project
/// without its own file saying so.
/// </param>
internal sealed record ProjectGraphRead(IReadOnlyList<GraphProject> Projects, string? Problem, bool InheritedTestHints)
{
    public static ProjectGraphRead Unknown(string problem) => new([], problem, InheritedTestHints: false);
}
