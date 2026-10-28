using System.Text.Json;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Testing;

/// <summary>
/// Finds the test applications red/green and trace can drive under a root after a build: xUnit v3 applications
/// (<c>IsTestingPlatformApplication</c>, <c>xunit.v3.core.dll</c> in the output) and xUnit v2 test projects on VSTest
/// (<c>IsTestProject</c>, <c>xunit.core.dll</c> in the output).
/// </summary>
internal static class TestApps
{
    private const int MaxDetailLength = 300;

    private static readonly string[] Properties =
        ["-getProperty:IsTestingPlatformApplication", "-getProperty:IsTestProject", "-getProperty:TargetPath", "-getProperty:TargetFrameworks"];

    /// <summary>
    /// The projects are evaluated in parallel (<c>dotnet msbuild -getProperty</c> only evaluates; it builds nothing), within
    /// <paramref name="timeLimit"/> for all of them together. A project whose evaluation fails or runs out of time is reported as
    /// unevaluated, never taken for a project without tests; one that evaluates and is no xUnit test application is left out.
    /// </summary>
    /// <param name="preferredFramework">For multi-targeted test projects: the framework to run (<c>gates.test.framework</c>); else the newest one.</param>
    public static async Task<TestAppDiscovery> FindAsync(string root, string? preferredFramework, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        string[] projects = [.. Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories).Where(path => !IsBuildOutput(root, path)).Order(StringComparer.Ordinal)];
        ProjectEvaluation[] evaluations = new ProjectEvaluation[projects.Length];
        TimeBudget budget = new(timeLimit, TimeProvider.System);
        ParallelOptions options = new() { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken };
        await Parallel.ForEachAsync(
            Enumerable.Range(0, projects.Length),
            options,
            async (index, token) => evaluations[index] = await FindOneAsync(root, projects[index], preferredFramework, budget, token));
        return new TestAppDiscovery(
            [.. evaluations.Select(evaluation => evaluation.App).OfType<TestApp>()],
            [.. evaluations.Select(evaluation => evaluation.Unevaluated).OfType<UnevaluatedProject>()]);
    }

    private static async Task<ProjectEvaluation> FindOneAsync(string root, string project, string? preferredFramework, TimeBudget budget, CancellationToken cancellationToken)
    {
        ProcessResult properties = await ProcessRunner.RunAsync("dotnet", ["msbuild", project, .. Properties], root, budget.Left, cancellationToken);
        if (Unevaluated(root, project, properties) is UnevaluatedProject unevaluated)
        {
            return ProjectEvaluation.Of(unevaluated);
        }

        if (Parse(properties.Output) is not (true, string targetPath, string frameworks))
        {
            return ProjectEvaluation.NotATestApp;
        }

        // A multi-targeted project has no TargetPath of its own; ask again for one framework.
        if (targetPath.Length == 0 && ChooseFramework(frameworks, preferredFramework) is string framework)
        {
            ProcessResult single = await ProcessRunner.RunAsync(
                "dotnet", ["msbuild", project, $"-p:TargetFramework={framework}", .. Properties], root, budget.Left, cancellationToken);
            if (Unevaluated(root, project, single) is UnevaluatedProject unevaluatedFramework)
            {
                return ProjectEvaluation.Of(unevaluatedFramework);
            }

            targetPath = Parse(single.Output) is (true, string path, _) ? path : string.Empty;
        }

        if (targetPath.Length == 0)
        {
            return ProjectEvaluation.NotATestApp;
        }

        string output = Path.GetDirectoryName(targetPath)!;
        if (File.Exists(Path.Combine(output, "xunit.v3.core.dll")))
        {
            return ProjectEvaluation.Of(new TestApp(Path.GetDirectoryName(project)!, targetPath));
        }

        return File.Exists(Path.Combine(output, "xunit.core.dll"))
            ? ProjectEvaluation.Of(new TestApp(Path.GetDirectoryName(project)!, targetPath, RunnerMode.VSTest))
            : ProjectEvaluation.NotATestApp;
    }

    /// <summary>The evaluation as an unevaluated project when it timed out, exited non-zero or printed no properties; null when it can be read.</summary>
    private static UnevaluatedProject? Unevaluated(string root, string project, ProcessResult evaluation)
    {
        if (evaluation.TimedOut)
        {
            return new UnevaluatedProject(project, ProjectEvaluationFailure.TimedOut, string.Empty);
        }

        if (evaluation.ExitCode != 0)
        {
            return new UnevaluatedProject(project, ProjectEvaluationFailure.Failed, FirstLine(root, evaluation.Combined, $"dotnet msbuild exited {evaluation.ExitCode}"));
        }

        return IsReadable(evaluation.Output) ? null : new UnevaluatedProject(project, ProjectEvaluationFailure.Failed, "dotnet msbuild printed no properties to read");
    }

    private static bool IsReadable(string output)
    {
        try
        {
            return Parse(output) is not null;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }

    private static string FirstLine(string root, string text, string fallback)
    {
        string? line = text.Split('\n').Select(candidate => candidate.Trim()).FirstOrDefault(candidate => candidate.Length > 0);
        if (line is null)
        {
            return fallback;
        }

        line = line.Replace(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, string.Empty, StringComparison.Ordinal);
        return line.Length <= MaxDetailLength ? line : line[..MaxDetailLength] + "…";
    }

    private static (bool IsTestApp, string TargetPath, string Frameworks)? Parse(string output)
    {
        int start = output.IndexOf('{', StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(output[start..]);
        JsonElement properties = document.RootElement.GetProperty("Properties");
        bool isTestApp = string.Equals(properties.GetProperty("IsTestingPlatformApplication").GetString(), "true", StringComparison.OrdinalIgnoreCase)
            || (properties.TryGetProperty("IsTestProject", out JsonElement testProject) && string.Equals(testProject.GetString(), "true", StringComparison.OrdinalIgnoreCase));
        return (isTestApp, properties.GetProperty("TargetPath").GetString() ?? string.Empty, properties.GetProperty("TargetFrameworks").GetString() ?? string.Empty);
    }

    /// <summary>The preferred framework when the project targets it, else the newest <c>netN.M</c> (the SDK's own runtime is the likeliest to be installed).</summary>
    public static string? ChooseFramework(string frameworks, string? preferred)
    {
        string[] targets = frameworks.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (preferred is not null && targets.Contains(preferred, StringComparer.OrdinalIgnoreCase))
        {
            return preferred;
        }

        return targets
            .Select(target => (Target: target, Version: Version.TryParse(target.StartsWith("net", StringComparison.OrdinalIgnoreCase) ? target[3..].Split('-')[0] : string.Empty, out Version? version) ? version : null))
            .Where(candidate => candidate.Version is not null && candidate.Version.Major >= 5)
            .OrderByDescending(candidate => candidate.Version)
            .Select(candidate => candidate.Target)
            .FirstOrDefault();
    }

    private static bool IsBuildOutput(string root, string path)
    {
        string[] segments = Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment => segment is "bin" or "obj" or "node_modules" || segment.StartsWith('.'));
    }
}
