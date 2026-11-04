using Lockflight.Cli.Gates;

namespace Lockflight.Cli.Testing;

/// <summary>
/// Command lines for a test application's tests: the one the engine's runner starts (<see cref="EngineRun"/>), and the plain
/// <c>dotnet test</c> a person would type (<see cref="DotnetTest"/>), on the platform the nearest <c>global.json</c> chooses
/// (<see cref="TestRunnerDetection"/>), filtered to the given methods.
/// </summary>
internal static class TestCommands
{
    /// <summary>
    /// The engine runner's command for the methods, without its report options: run in the app's output folder. An NUnit run selects
    /// them with a temporary run-settings file, shown by its <c>Where</c> clause.
    /// </summary>
    public static TestCommand EngineRun(TestApp app, IReadOnlyList<string> methodFilters)
    {
        List<string> arguments = app.Mode switch
        {
            RunnerMode.Mtp => [app.AssemblyPath, .. methodFilters.SelectMany(method => new[] { "--filter-method", method })],
            RunnerMode.VSTest or RunnerMode.MSTest => ["test", app.AssemblyPath, "--filter", FullyQualifiedNames(methodFilters)],
            RunnerMode.MSTestMtp => [app.AssemblyPath, "--filter", FullyQualifiedNames(methodFilters)],
            RunnerMode.NUnit => ["test", app.AssemblyPath, "--settings", $"<run settings: {NUnitRunner.Where(methodFilters)}>"],
            RunnerMode.NUnitMtp => [app.AssemblyPath, "--settings", $"<run settings: {NUnitRunner.Where(methodFilters)}>"],
            _ => [app.AssemblyPath, "-noLogo", "-noColor", .. methodFilters.SelectMany(method => new[] { "-method", method })],
        };
        return new TestCommand(app.OutputDirectory, arguments);
    }

    /// <summary>
    /// <c>dotnet test</c> on the app's project, filtered to the methods; null when the project file is unknown. It runs in the project's
    /// directory, so the SDK reads the same <c>global.json</c> the platform was chosen from. <paramref name="noBuild"/> adds
    /// <c>--no-build</c>, for a run right after the engine's own build.
    /// </summary>
    public static TestCommand? DotnetTest(TestApp app, IReadOnlyList<string> methodFilters, bool noBuild)
    {
        if (app.ProjectFile is null)
        {
            return null;
        }

        string project = Path.GetFileName(app.ProjectFile);
        TestRunnerKind platform = TestRunnerDetection.For(app.ProjectDirectory);
        List<string> arguments = platform == TestRunnerKind.MicrosoftTestingPlatform ? ["test", "--project", project] : ["test", project];
        if (noBuild)
        {
            arguments.Add("--no-build");
        }

        if (app.Framework is string framework)
        {
            arguments.AddRange(["--framework", framework]);
        }

        bool xunitOnTestingPlatform = platform == TestRunnerKind.MicrosoftTestingPlatform && app.Mode is RunnerMode.Unknown or RunnerMode.Native or RunnerMode.Mtp;
        arguments.AddRange(xunitOnTestingPlatform ? methodFilters.SelectMany(method => new[] { "--filter-method", method })
            : app.Mode is RunnerMode.NUnit or RunnerMode.NUnitMtp ? ["--filter", NUnitNames(methodFilters)]
            : ["--filter", FullyQualifiedNames(methodFilters)]);
        return new TestCommand(app.ProjectDirectory, arguments);
    }

    /// <summary>
    /// NUnit names a parameterized test with its arguments (<c>Shop.Tests.Prices.Costs(2)</c>), so each method is matched as itself
    /// or as the start of such a name (<c>FullyQualifiedName~Shop.Tests.Prices.Costs(</c>).
    /// </summary>
    private static string NUnitNames(IReadOnlyList<string> methodFilters) =>
        string.Join('|', methodFilters.Select(method => $"FullyQualifiedName={VSTestRunner.Escape(method)}|FullyQualifiedName~{VSTestRunner.Escape(method + "(")}"));

    /// <summary><c>FullyQualifiedName=A|FullyQualifiedName=B</c>, escaped for VSTest's filter syntax.</summary>
    private static string FullyQualifiedNames(IReadOnlyList<string> methodFilters) =>
        string.Join('|', methodFilters.Select(method => $"FullyQualifiedName={VSTestRunner.Escape(method)}"));
}
