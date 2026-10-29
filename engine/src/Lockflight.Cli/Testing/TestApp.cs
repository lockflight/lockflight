namespace Lockflight.Cli.Testing;

/// <summary>A built xUnit or NUnit test application: its project directory, the assembly the runner executes, and how it is run.</summary>
internal sealed record TestApp(string ProjectDirectory, string AssemblyPath, RunnerMode Mode = RunnerMode.Unknown)
{
    public string AssemblyName => Path.GetFileNameWithoutExtension(AssemblyPath);
}
