using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Canaries;

/// <summary>
/// A throwaway Lockflight project in a new temporary directory, where the canaries plant their violations. Nothing outside it is
/// read or written: commands run in-process against it with a fixed environment whose home directory is inside the scratch area, so
/// a refusal that fails cannot reach the person's project or their user install. Deleted on dispose.
/// </summary>
internal sealed class CanaryScratch : IDisposable
{
    private const string MinimalManifest = "version: 1\n";

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-canary-");

    public CanaryScratch(TimeProvider time)
    {
        Time = time;
        Project = Path.Combine(root.FullName, "project");
        Home = Path.Combine(root.FullName, "home");
        Directory.CreateDirectory(Path.Combine(Project, "src"));
        Directory.CreateDirectory(Home);
        File.WriteAllText(Path.Combine(Project, Manifest.FileName), MinimalManifest);
    }

    /// <summary>The scratch project's root, with a minimal <c>lockflight.yaml</c> and an empty <c>src/</c>.</summary>
    public string Project { get; }

    public string Home { get; }

    public TimeProvider Time { get; }

    /// <summary>Runs an engine command in the scratch project as an agent's session would: <c>CLAUDECODE=1</c>.</summary>
    public Task<CanaryRun> RunAsAgentAsync(string[] args, string input, CancellationToken cancellationToken) =>
        RunAsync(args, input, underAgent: true, cancellationToken);

    /// <summary>Runs an engine command in the scratch project as a person in their own terminal.</summary>
    public Task<CanaryRun> RunAsPersonAsync(string[] args, CancellationToken cancellationToken) =>
        RunAsync(args, string.Empty, underAgent: false, cancellationToken);

    public string PathOf(string relativePath) => Path.Combine(Project, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public void Dispose()
    {
        try
        {
            root.Delete(recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A build server can hold a file a moment longer (Windows); the operating system cleans its temporary directory.
        }
    }

    private async Task<CanaryRun> RunAsync(string[] args, string input, bool underAgent, CancellationToken cancellationToken)
    {
        Dictionary<string, string> variables = new(StringComparer.Ordinal)
        {
            [EnvironmentVariables.Home] = Home,
            [EnvironmentVariables.UserProfile] = Home,
        };
        if (underAgent)
        {
            variables[EnvironmentVariables.ClaudeCode] = "1";
        }

        await using StringWriter output = new();
        await using StringWriter error = new();
        CliContext context = new(new StringReader(input), output, error, Project, Time) { Environment = EnvironmentReader.Of(variables) };
        ExitCode exitCode = await CliApplication.RunAsync(args, context, cancellationToken);
        return new CanaryRun(exitCode, output.ToString().Trim(), error.ToString().Trim());
    }
}
