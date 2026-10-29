namespace Lockflight.Cli.Gates.Affected;

/// <summary>
/// Which projects the stop profile's build and test steps run, and why, chosen by <see cref="ProjectSelector"/>. An affected
/// selection runs through a solution filter (<see cref="FilterFile"/>) that lists them; everything runs the target's solution or
/// <c>build:</c> file as before.
/// </summary>
/// <param name="Projects">The affected projects in the solution's order; empty for everything.</param>
/// <param name="SolutionProjects">How many projects the solution lists; 0 when it was not read.</param>
/// <param name="FilterFile">The solution filter (<c>.slnf</c>) of the affected projects; null for everything.</param>
/// <param name="RunsTests">False when no affected project is a test project, so the test step has nothing to run.</param>
internal sealed record ProjectSelection(
    SelectionKind Kind, string Reason, IReadOnlyList<AffectedProject> Projects, int SolutionProjects, string? FilterFile, bool RunsTests)
{
    private const int MaxListed = 8;

    public static ProjectSelection Everything(string reason) => new(SelectionKind.Everything, reason, [], 0, null, RunsTests: true);

    /// <summary>The step's detail line: what runs and why, each affected project with its reason.</summary>
    public string Describe()
    {
        if (Kind == SelectionKind.Everything)
        {
            return $"every project: {Reason}";
        }

        string listed = string.Join("; ", Projects.Take(MaxListed).Select(project => $"{project.Name} ({project.Reason})"));
        string more = Projects.Count > MaxListed ? $"; and {Projects.Count - MaxListed} more" : string.Empty;
        return $"{Projects.Count(project => project.SolutionEntry is not null)} of {SolutionProjects} projects, by the uncommitted changes " +
            $"(--full runs every project): {listed}{more}";
    }

    /// <summary>What the run judged when no test ran, for the <c>Scope:</c> line; null for everything.</summary>
    public string? Scope => Kind == SelectionKind.Affected
        ? $"{Projects.Count(project => project.SolutionEntry is not null)} of {SolutionProjects} projects (affected)"
        : null;
}
