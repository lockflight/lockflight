using Lockflight.Cli.Ci;
using Lockflight.Cli.Config;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Lifecycle.Fix;

/// <summary>
/// Resolves the target branch of <c>lockflight red</c>: <c>--base</c> as given, else <c>review.target</c>, else the branch
/// <c>origin/HEAD</c> names, else <c>main</c>, taken as the remote-tracking <c>origin/&lt;name&gt;</c> when it exists. The judge
/// forks from the remote tip; a stale local branch would count tests already merged there as new.
/// </summary>
internal static class RedBase
{
    public const string Option = "--base";

    private const string DefaultBranch = "main";
    private const string Remote = "origin/";

    public static async Task<RedBaseResolution> ResolveAsync(string root, Manifest manifest, string? given, CancellationToken cancellationToken)
    {
        if (given is not null)
        {
            return await JudgeGit.ResolveAsync(root, given, cancellationToken) is string commit
                ? RedBaseResolution.Resolved(given, commit, null)
                : RedBaseResolution.Failed($"{Option} '{given}' names no commit here; fetch it, or name the target branch another way");
        }

        string name = manifest.Review.Target ?? await Git.DefaultBranchAsync(root, DefaultBranch, cancellationToken);
        string remote = name.StartsWith(Remote, StringComparison.Ordinal) ? name : Remote + name;
        string? local = name.StartsWith(Remote, StringComparison.Ordinal) ? null : await JudgeGit.ResolveAsync(root, $"refs/heads/{name}", cancellationToken);
        if (await JudgeGit.ResolveAsync(root, $"refs/remotes/{remote}", cancellationToken) is string remoteCommit)
        {
            string? note = local is not null && local != remoteCommit
                ? $"target: {remote} ({CommitIds.Short(remoteCommit)}), not your local {name} ({CommitIds.Short(local)}), which differs from it; CI forks from the remote tip. Pass {Option} {name} to compare with the local branch."
                : null;
            return RedBaseResolution.Resolved(remote, remoteCommit, note);
        }

        return local is not null
            ? RedBaseResolution.Resolved(name, local, null)
            : RedBaseResolution.Failed($"the target branch '{name}' names no commit here; fetch it, or pass {Option} <ref>");
    }
}
