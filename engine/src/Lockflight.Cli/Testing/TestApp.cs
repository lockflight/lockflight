namespace Lockflight.Cli.Testing;

/// <summary>
/// A built xUnit, NUnit or MSTest test application: its project directory, the assembly the runner executes, and how it is run. Every
/// runner starts it in its output folder (<see cref="OutputDirectory"/>) with <see cref="Environment"/> set, as <c>dotnet test</c> does.
/// </summary>
internal sealed record TestApp(string ProjectDirectory, string AssemblyPath, RunnerMode Mode = RunnerMode.Unknown)
{
    public string AssemblyName => Path.GetFileNameWithoutExtension(AssemblyPath);

    /// <summary>The folder the assembly was built to: the test process's working directory under <c>dotnet test</c>, and so under every runner here.</summary>
    public string OutputDirectory => Path.GetDirectoryName(AssemblyPath) ?? ProjectDirectory;

    /// <summary>The <c>test.stopFilter</c> of the directory the app was built in: what a whole-suite regression run leaves out; null for none.</summary>
    public string? StopFilter { get; init; }

    /// <summary>The project file the app was built from, for a <c>dotnet test</c> command line; null when unknown.</summary>
    public string? ProjectFile { get; init; }

    /// <summary>The target framework the app was built for when the project targets several; null for a single-target project.</summary>
    public string? Framework { get; init; }

    /// <summary>The project's <c>.runsettings</c> (<c>RunSettingsFilePath</c>), whose environment variables <c>dotnet test</c> applies; null for none.</summary>
    public string? RunSettingsFile { get; init; }

    /// <summary>
    /// The variables every run of this app sets: those of <see cref="RunSettingsFile"/>'s <c>RunConfiguration/EnvironmentVariables</c>
    /// when <c>dotnet test</c> applies them to this app (<see cref="RunSettingsEnvironment"/>); empty otherwise.
    /// </summary>
    public IReadOnlyDictionary<string, string?> Environment { get; init; } = RunSettingsEnvironment.None;
}
