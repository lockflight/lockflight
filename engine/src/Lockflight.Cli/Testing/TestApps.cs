using System.Text.Json;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Testing;

/// <summary>
/// Finds the test applications red/green and trace can drive under a root after a build: xUnit v3 applications
/// (<c>IsTestingPlatformApplication</c>, <c>xunit.v3.core.dll</c> in the output), xUnit v2 test projects on VSTest
/// (<c>IsTestProject</c>, <c>xunit.core.dll</c> in the output), and NUnit (<c>nunit.framework.dll</c>) and MSTest
/// (<c>MSTest.TestFramework.dll</c>, or <c>Microsoft.VisualStudio.TestPlatform.TestFramework.dll</c> before MSTest 4) test projects,
/// on Microsoft.Testing.Platform when the project is a testing-platform application, else on VSTest.
/// </summary>
internal static class TestApps
{
    private const int MaxDetailLength = 300;

    /// <summary>
    /// Evaluations at once: each is a <c>dotnet msbuild</c> process of a few hundred megabytes, and red/green, trace and the CI judge
    /// all run them, so one per core would exhaust the memory of a large machine running a solution with many projects.
    /// </summary>
    private const int MaxParallelEvaluations = 4;

    /// <summary>
    /// The properties <c>dotnet test</c> on VSTest reads to add data collectors, adapter paths, loggers or another run settings file to
    /// the run, which the host-tampering check lists.
    /// </summary>
    public static readonly string[] VSTestProperties = ["VSTestCollect", "VSTestTestAdapterPath", "VSTestLogger", "VSTestSetting"];

    private static readonly string[] MSTestFrameworkAssemblies = ["MSTest.TestFramework.dll", "Microsoft.VisualStudio.TestPlatform.TestFramework.dll"];

    private static readonly string[] Properties =
        ["-getProperty:IsTestingPlatformApplication", "-getProperty:IsTestProject", "-getProperty:TargetPath", "-getProperty:TargetFrameworks",
         "-getProperty:RunSettingsFilePath", .. VSTestProperties.Select(property => $"-getProperty:{property}")];

    /// <summary>
    /// The projects are evaluated in parallel, at most <see cref="MaxParallelEvaluations"/> at a time (<c>dotnet msbuild -getProperty</c> only evaluates; it builds nothing), within
    /// <paramref name="timeLimit"/> for all of them together. A project whose evaluation fails or runs out of time is reported as
    /// unevaluated, never taken for a project without tests; one that evaluates and is no test application of these frameworks is left out.
    /// </summary>
    /// <param name="preferredFramework">For multi-targeted test projects: the framework to run (<c>gates.test.framework</c>); else the newest one.</param>
    public static async Task<TestAppDiscovery> FindAsync(string root, string? preferredFramework, TimeSpan timeLimit, TimeProvider time, CancellationToken cancellationToken)
    {
        string[] projects = [.. Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories).Where(path => !IsBuildOutput(root, path)).Order(StringComparer.Ordinal)];
        ProjectEvaluation[] evaluations = new ProjectEvaluation[projects.Length];
        TimeBudget budget = new(timeLimit, time);
        ParallelOptions options = new() { MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, MaxParallelEvaluations), CancellationToken = cancellationToken };
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
        if (budget.IsSpent)
        {
            return ProjectEvaluation.Of(new UnevaluatedProject(project, ProjectEvaluationFailure.TimedOut, string.Empty));
        }

        ProcessResult properties = await CompleteOutput.RunAsync(() => ProcessRunner.RunAsync("dotnet", ["msbuild", project, .. Properties], root, budget.Left, cancellationToken));
        if (Unevaluated(root, project, properties) is UnevaluatedProject unevaluated)
        {
            return ProjectEvaluation.Of(unevaluated);
        }

        if (Parse(properties.Output) is not (true, bool onTestingPlatform, string targetPath, string frameworks, string runSettings, IReadOnlyDictionary<string, string> vstest))
        {
            return ProjectEvaluation.NotATestApp;
        }

        // A multi-targeted project has no TargetPath of its own; ask again for one framework.
        string? chosenFramework = null;
        if (targetPath.Length == 0 && ChooseFramework(frameworks, preferredFramework) is string framework)
        {
            chosenFramework = framework;
            ProcessResult single = await CompleteOutput.RunAsync(() => ProcessRunner.RunAsync(
                "dotnet", ["msbuild", project, $"-p:TargetFramework={framework}", .. Properties], root, budget.Left, cancellationToken));
            if (Unevaluated(root, project, single) is UnevaluatedProject unevaluatedFramework)
            {
                return ProjectEvaluation.Of(unevaluatedFramework);
            }

            (targetPath, runSettings, vstest) = Parse(single.Output) is (true, _, string path, _, string settings, IReadOnlyDictionary<string, string> singleVSTest)
                ? (path, settings, singleVSTest)
                : (string.Empty, runSettings, vstest);
        }

        if (targetPath.Length == 0)
        {
            return ProjectEvaluation.NotATestApp;
        }

        string directory = Path.GetDirectoryName(project)!;
        return Kind(Path.GetDirectoryName(targetPath)!, onTestingPlatform) is RunnerMode mode
            ? ProjectEvaluation.Of(Configured(new TestApp(directory, targetPath, mode) { ProjectFile = project, Framework = chosenFramework, VSTestProperties = vstest }, runSettings))
            : ProjectEvaluation.NotATestApp;
    }

    /// <summary>The runner mode the framework in <paramref name="output"/> starts with (an xUnit v3 app is probed); null for no known framework.</summary>
    private static RunnerMode? Kind(string output, bool onTestingPlatform)
    {
        if (File.Exists(Path.Combine(output, "xunit.v3.core.dll")))
        {
            return RunnerMode.Unknown;
        }

        if (File.Exists(Path.Combine(output, "xunit.core.dll")))
        {
            return RunnerMode.VSTest;
        }

        if (File.Exists(Path.Combine(output, "nunit.framework.dll")))
        {
            return onTestingPlatform ? RunnerMode.NUnitMtp : RunnerMode.NUnit;
        }

        return MSTestFrameworkAssemblies.Any(assembly => File.Exists(Path.Combine(output, assembly)))
            ? onTestingPlatform ? RunnerMode.MSTestMtp : RunnerMode.MSTest
            : null;
    }

    /// <summary>
    /// The app with the project's run settings (<c>RunSettingsFilePath</c>, relative to the project directory) and the variables they
    /// set, when <c>dotnet test</c> would apply them to it (<see cref="RunSettingsEnvironment.AppliesTo"/>).
    /// </summary>
    public static TestApp Configured(TestApp app, string runSettings)
    {
        if (runSettings.Length == 0)
        {
            return app;
        }

        string file = Path.GetFullPath(runSettings, app.ProjectDirectory);
        if (!File.Exists(file))
        {
            return app;
        }

        return RunSettingsEnvironment.AppliesTo(TestRunnerDetection.For(app.ProjectDirectory))
            ? app with { RunSettingsFile = file, Environment = RunSettingsEnvironment.Read(file) }
            : app with { RunSettingsFile = file };
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

        if (evaluation.OutputHeld)
        {
            return new UnevaluatedProject(project, ProjectEvaluationFailure.Failed, CompleteOutput.Problem("dotnet msbuild"));
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

    private static (bool IsTestApp, bool OnTestingPlatform, string TargetPath, string Frameworks, string RunSettings, IReadOnlyDictionary<string, string> VSTest)? Parse(string output)
    {
        int start = output.IndexOf('{', StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(output[start..]);
        JsonElement properties = document.RootElement.GetProperty("Properties");
        bool onTestingPlatform = string.Equals(properties.GetProperty("IsTestingPlatformApplication").GetString(), "true", StringComparison.OrdinalIgnoreCase);
        bool isTestApp = onTestingPlatform
            || (properties.TryGetProperty("IsTestProject", out JsonElement testProject) && string.Equals(testProject.GetString(), "true", StringComparison.OrdinalIgnoreCase));
        string runSettings = properties.TryGetProperty("RunSettingsFilePath", out JsonElement settings) ? settings.GetString() ?? string.Empty : string.Empty;
        Dictionary<string, string> vstest = new(StringComparer.Ordinal);
        foreach (string name in VSTestProperties)
        {
            if (properties.TryGetProperty(name, out JsonElement value) && value.GetString()?.Trim() is { Length: > 0 } text)
            {
                vstest[name] = text;
            }
        }

        return (isTestApp, onTestingPlatform, properties.GetProperty("TargetPath").GetString() ?? string.Empty, properties.GetProperty("TargetFrameworks").GetString() ?? string.Empty, runSettings.Trim(), vstest);
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
