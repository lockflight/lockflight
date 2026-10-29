using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Canaries;

/// <summary>The git a canary needs in its scratch project: a repository and commits under a canary identity.</summary>
internal static class CanaryGit
{
    private const string GitName = "Lockflight canary";
    private const string GitEmail = "canary@example.com";

    private static readonly TimeSpan GitTimeLimit = TimeSpan.FromSeconds(60);

    /// <summary>Creates the repository when <paramref name="initialize"/>, then commits everything; the reason when git failed, else null.</summary>
    public static async Task<string?> CommitAllAsync(string directory, string message, bool initialize, CancellationToken cancellationToken)
    {
        string[][] steps =
        [
            .. initialize ? [["init", "-q"]] : Array.Empty<string[]>(),
            ["add", "-A"],
            ["-c", $"user.name={GitName}", "-c", $"user.email={GitEmail}", "-c", "commit.gpgsign=false", "commit", "-q", "-m", message],
        ];
        foreach (string[] step in steps)
        {
            ProcessResult git = await ProcessRunner.RunAsync("git", step, directory, GitTimeLimit, cancellationToken);
            if (git.ExitCode != 0)
            {
                return $"git {step[0]} failed (exit {git.ExitCode}): {git.Combined.Trim()}";
            }
        }

        return null;
    }
}
