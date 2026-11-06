namespace Lockflight.Cli.Processes;

/// <summary>
/// Stops the .NET build servers (the MSBuild server, the compiler server VBCSCompiler and the Razor build server) with
/// <c>dotnet build-server shutdown</c>. The engine starts builds with the servers turned off, but a project can turn them back
/// on; a server left running would keep the project's analyzers and source generators alive after its build. Run in the
/// directory that was built, so the same SDK answers.
/// </summary>
internal static class BuildServers
{
    private static readonly TimeSpan ShutdownTimeLimit = TimeSpan.FromSeconds(60);

    /// <summary>Asks the servers to stop; a failure is ignored (there may be no SDK, or nothing to stop).</summary>
    public static async Task ShutdownAsync(string directory, CancellationToken cancellationToken)
    {
        try
        {
            await ProcessRunner.RunAsync("dotnet", ["build-server", "shutdown"], directory, ShutdownTimeLimit, cancellationToken);
        }
        catch (ExecutableNotFoundException)
        {
            // Without the .NET SDK nothing was built, so no server runs.
        }
    }
}
