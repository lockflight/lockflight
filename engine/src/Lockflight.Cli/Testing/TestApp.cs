namespace Lockflight.Cli.Testing;

/// <summary>A built xUnit, NUnit or MSTest test application: its project directory, the assembly the runner executes, and how it is run.</summary>
internal sealed record TestApp(string ProjectDirectory, string AssemblyPath, RunnerMode Mode = RunnerMode.Unknown)
{
    public string AssemblyName => Path.GetFileNameWithoutExtension(AssemblyPath);

    /// <summary>The <c>test.stopFilter</c> of the directory the app was built in: what a whole-suite regression run leaves out; null for none.</summary>
    public string? StopFilter { get; init; }
}
