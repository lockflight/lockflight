using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Lifecycle.RedGreen.Snapshots;

/// <summary>
/// Runs git for snapshots and their worktrees without the variables that point git at another repository, work tree or index
/// (<c>GIT_DIR</c>, <c>GIT_WORK_TREE</c>, <c>GIT_INDEX_FILE</c>, …): an engine started from a git hook or a wrapper inherits them, and a
/// <c>reset --hard</c> meant for the temporary worktree would otherwise reach the person's own.
/// </summary>
internal static class SnapshotGit
{
    public const string IndexVariable = "GIT_INDEX_FILE";

    private const string Executable = "git";

    private static readonly string[] Redirecting =
        ["GIT_DIR", "GIT_WORK_TREE", IndexVariable, "GIT_COMMON_DIR", "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_PREFIX", "GIT_NAMESPACE"];

    public static Task<ProcessResult> RunAsync(string[] arguments, string directory, TimeSpan timeLimit, CancellationToken cancellationToken) =>
        RunAsync(arguments, directory, timeLimit, new Dictionary<string, string>(StringComparer.Ordinal), cancellationToken);

    /// <summary>Runs git with <paramref name="variables"/> set on top of the cleared ones (a private <see cref="IndexVariable"/>, an identity).</summary>
    public static Task<ProcessResult> RunAsync(
        string[] arguments, string directory, TimeSpan timeLimit, IReadOnlyDictionary<string, string> variables, CancellationToken cancellationToken)
    {
        Dictionary<string, string?> environment = Redirecting.ToDictionary(name => name, string? (_) => null, StringComparer.Ordinal);
        foreach ((string name, string value) in variables)
        {
            environment[name] = value;
        }

        return ProcessRunner.RunAsync(Executable, arguments, directory, timeLimit, environment, cancellationToken);
    }

    /// <summary>The trimmed output of a git call that succeeded with some; null otherwise.</summary>
    public static async Task<string?> OutputAsync(
        string[] arguments, string directory, TimeSpan timeLimit, IReadOnlyDictionary<string, string> variables, CancellationToken cancellationToken)
    {
        ProcessResult result = await RunAsync(arguments, directory, timeLimit, variables, cancellationToken);
        return result.ExitCode == 0 && result.Output.Trim() is { Length: > 0 } output ? output : null;
    }

    public static Task<string?> OutputAsync(string[] arguments, string directory, TimeSpan timeLimit, CancellationToken cancellationToken) =>
        OutputAsync(arguments, directory, timeLimit, new Dictionary<string, string>(StringComparer.Ordinal), cancellationToken);
}
