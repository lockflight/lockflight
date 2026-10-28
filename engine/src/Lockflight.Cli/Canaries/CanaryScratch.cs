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

    /// <summary>What an agent's shell has: <c>CLAUDECODE=1</c> and nothing that says a person attends the session.</summary>
    private static readonly Dictionary<string, string> AgentSession = new(StringComparer.Ordinal) { [EnvironmentVariables.ClaudeCode] = "1" };

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
        RunAsync(args, input, AgentSession, Time, cancellationToken);

    /// <summary>Runs an engine command in the scratch project as a person in their own terminal.</summary>
    public Task<CanaryRun> RunAsPersonAsync(string[] args, CancellationToken cancellationToken) =>
        RunAsync(args, string.Empty, new Dictionary<string, string>(StringComparer.Ordinal), Time, cancellationToken);

    /// <summary>
    /// Runs an engine command in the scratch project inside a Claude Code session described by <paramref name="session"/> (the
    /// variables Claude Code sets, <c>CLAUDECODE=1</c> among them), on the clock <paramref name="clock"/>.
    /// </summary>
    public Task<CanaryRun> RunInSessionAsync(string[] args, string input, IReadOnlyDictionary<string, string> session, TimeProvider clock, CancellationToken cancellationToken) =>
        RunAsync(args, input, session, clock, cancellationToken);

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

    private async Task<CanaryRun> RunAsync(string[] args, string input, IReadOnlyDictionary<string, string> session, TimeProvider clock, CancellationToken cancellationToken)
    {
        Dictionary<string, string> variables = new(session, StringComparer.Ordinal)
        {
            [EnvironmentVariables.Home] = Home,
            [EnvironmentVariables.UserProfile] = Home,
        };

        await using StringWriter output = new();
        await using StringWriter error = new();
        CliContext context = new(new StringReader(input), output, error, Project, clock) { Environment = EnvironmentReader.Of(variables) };
        ExitCode exitCode = await CliApplication.RunAsync(args, context, cancellationToken);
        return new CanaryRun(exitCode, output.ToString().Trim(), error.ToString().Trim());
    }
}
