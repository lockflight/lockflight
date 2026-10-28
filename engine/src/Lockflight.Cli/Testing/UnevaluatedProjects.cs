namespace Lockflight.Cli.Testing;

/// <summary>
/// The message for projects that could not be evaluated: a test project among them would be left out of red/green and trace, so they
/// earn nothing until each evaluates.
/// </summary>
internal static class UnevaluatedProjects
{
    private const int MaxProjectsNamed = 3;

    public static string Describe(string root, IReadOnlyList<UnevaluatedProject> unevaluated, TimeSpan timeLimit)
    {
        string named = string.Join("; ", unevaluated.Take(MaxProjectsNamed).Select(project => Describe(root, project)));
        string more = unevaluated.Count > MaxProjectsNamed ? $"; and {unevaluated.Count - MaxProjectsNamed} more" : string.Empty;
        string subject = unevaluated.Count == 1 ? "a project could not be evaluated" : $"{unevaluated.Count} projects could not be evaluated";
        string timeout = unevaluated.Any(project => project.Failure == ProjectEvaluationFailure.TimedOut)
            ? $" A timeout means gates.timeoutSeconds ({(int)timeLimit.TotalSeconds} s, shared by the evaluations of all projects) is too short for this tree: raise it in lockflight.yaml."
            : string.Empty;
        return $"{subject}: {named}{more}. A test project that cannot be evaluated would be left out, so nothing is earned until each evaluates. "
            + "Fix each so 'dotnet msbuild <project> -getProperty:IsTestProject' succeeds, or remove a project that is not meant to build; "
            + $"no setting excludes a project from test discovery.{timeout}";
    }

    private static string Describe(string root, UnevaluatedProject project)
    {
        string path = Path.GetRelativePath(root, project.Project).Replace('\\', '/');
        return project.Failure == ProjectEvaluationFailure.TimedOut ? $"{path} (evaluation timed out)" : $"{path} (evaluation failed: {project.Detail})";
    }
}
