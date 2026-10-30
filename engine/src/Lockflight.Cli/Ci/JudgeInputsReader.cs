using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Turns the command line into <see cref="JudgeInputs"/>. The engine never reads platform variables: each template passes them as
/// flags, so a variable the pull request's tree could set cannot steer the run. Every commit must be a full id this clone has.
/// </summary>
internal static class JudgeInputsReader
{
    public const string HeadOption = "--head";
    public const string BaseOption = "--base";
    public const string BranchOption = "--branch";
    public const string PlatformOption = "--platform";
    public const string EventOption = "--event";
    public const string EvidenceOption = "--evidence";
    public const string ShellOption = "--shell";
    public const string NoReviewFlag = "--no-review-expected";

    /// <summary>Where a local run writes its evidence, relative to the repository root (git-ignored local state).</summary>
    public const string LocalEvidenceDirectory = ".lockflight/local/ci";

    private const string DefaultTarget = "main";

    public static async Task<JudgeInputsReading> ReadAsync(CommandOptions options, string workingDirectory, CancellationToken cancellationToken)
    {
        ProcessResult top = await ProcessRunner.RunAsync("git", ["rev-parse", "--show-toplevel"], workingDirectory, cancellationToken);
        if (top.ExitCode != 0 || top.Output.Trim() is not { Length: > 0 } root)
        {
            return Failed($"{workingDirectory} is not inside a git repository; run lockflight ci from the checkout of the pull request");
        }

        root = Path.GetFullPath(root);
        string platformName = options.Value(PlatformOption) ?? JudgePlatformNames.ToName(JudgePlatform.Local);
        if (JudgePlatformNames.Parse(platformName) is not JudgePlatform platform)
        {
            return Failed($"{PlatformOption} must be {JudgePlatformNames.Accepted}, not '{platformName}'");
        }

        string eventName = options.Value(EventOption) ?? JudgeEventNames.ToName(JudgeEvent.PullRequest);
        if (JudgeEventNames.Parse(eventName) is not JudgeEvent judgeEvent)
        {
            return Failed($"{EventOption} must be {JudgeEventNames.Accepted}, not '{eventName}'");
        }

        (string? head, string? headError) = await HeadAsync(options, root, platform, cancellationToken);
        if (head is null)
        {
            return Failed(headError!);
        }

        (string? baseCommit, string? baseError) = await BaseAsync(options, root, platform, judgeEvent, cancellationToken);
        if (baseError is not null)
        {
            return Failed(baseError);
        }

        if (platform == JudgePlatform.Local && await DirtyAsync(root, cancellationToken) is string dirty)
        {
            return Failed($"the working tree has uncommitted changes ({dirty}); the judge checks commits, so commit or stash them first");
        }

        if (options.Value(EvidenceOption) is not string evidence && platform != JudgePlatform.Local)
        {
            return Failed($"{EvidenceOption} <dir> is required on {platformName}: a directory outside the checkout, such as \"$RUNNER_TEMP/lockflight-evidence\"");
        }

        string evidenceDirectory = options.Value(EvidenceOption) is string given
            ? Path.GetFullPath(given, workingDirectory)
            : Path.Combine(root, LocalEvidenceDirectory.Replace('/', Path.DirectorySeparatorChar));
        string? shell = options.Value(ShellOption) is string shellPath ? Path.GetFullPath(shellPath, workingDirectory) : null;
        return new JudgeInputsReading(
            new JudgeInputs(root, head, baseCommit, options.Value(BranchOption), platform, judgeEvent, evidenceDirectory, shell, !options.Flag(NoReviewFlag)),
            Error: null);
    }

    private static async Task<(string? Head, string? Error)> HeadAsync(CommandOptions options, string root, JudgePlatform platform, CancellationToken cancellationToken)
    {
        if (options.Value(HeadOption) is not string head)
        {
            if (platform != JudgePlatform.Local)
            {
                return (null, $"{HeadOption} <sha> is required on {JudgePlatformNames.ToName(platform)}: the pull request's head commit, not the merge commit the platform checks out");
            }

            return await JudgeGit.ResolveAsync(root, "HEAD", cancellationToken) is string resolved
                ? (resolved, null)
                : (null, "HEAD names no commit; commit the change first");
        }

        return await CommitAsync(root, HeadOption, head, cancellationToken);
    }

    private static async Task<(string? Base, string? Error)> BaseAsync(CommandOptions options, string root, JudgePlatform platform, JudgeEvent judgeEvent, CancellationToken cancellationToken)
    {
        if (options.Value(BaseOption) is string given)
        {
            // A push gives the commit before it: all zeros for a new branch, or one a force-push removed. The gates then run without a base.
            if (judgeEvent != JudgeEvent.PullRequest && (CommitIds.IsZero(given) || (CommitIds.IsFull(given) && !await JudgeGit.HasCommitAsync(root, given, cancellationToken))))
            {
                return (null, null);
            }

            return await CommitAsync(root, BaseOption, given, cancellationToken);
        }

        if (judgeEvent != JudgeEvent.PullRequest)
        {
            return (null, null);
        }

        if (platform != JudgePlatform.Local)
        {
            return (null, $"{BaseOption} <sha> is required for a pull request: the tip of the target branch, fetched when the job runs");
        }

        string target = ManifestLocator.Discover(root).Manifest?.Review.Target ?? await Git.DefaultBranchAsync(root, DefaultTarget, cancellationToken);
        return await JudgeGit.ResolveAsync(root, target, cancellationToken) is string resolved
            ? (resolved, null)
            : (null, $"the target branch '{target}' names no commit here; fetch it or pass {BaseOption} <sha>");
    }

    private static async Task<(string? Commit, string? Error)> CommitAsync(string root, string option, string value, CancellationToken cancellationToken)
    {
        if (!CommitIds.IsFull(value))
        {
            return (null, $"{option} must be a full commit id (40 or 64 lower-case hex characters), not '{TerminalText.Clean(value)}'");
        }

        return await JudgeGit.HasCommitAsync(root, value, cancellationToken)
            ? (value, null)
            : (null, $"{option} {value} is not a commit in this clone; fetch it (on GitHub: fetch-depth: 0) and run again");
    }

    private static async Task<string?> DirtyAsync(string root, CancellationToken cancellationToken)
    {
        ProcessResult status = await ProcessRunner.RunAsync("git", ["-c", "core.quotePath=false", "status", "--porcelain", "--", ".", $":(exclude){LocalEvidenceDirectory}"], root, cancellationToken);
        string[] lines = status.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return status.ExitCode != 0 ? "git status failed"
            : lines.Length == 0 ? null
            : string.Join(", ", lines.Take(3).Select(line => line.Length > 3 ? line[3..] : line)) + (lines.Length > 3 ? $" and {lines.Length - 3} more" : string.Empty);
    }

    private static JudgeInputsReading Failed(string error) => new(null, error);
}
