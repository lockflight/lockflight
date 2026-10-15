using System.Diagnostics;
using Lockflight.Cli.Config;
using Lockflight.Cli.Paths;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Gates.Steps;

/// <summary>
/// Lists the protected files a pull request changes (pr profile, with a base ref). The hooks keep the agent out of them during a
/// session, but a commit can still reach them by other means; these changes need a human reviewer, typically via CODEOWNERS.
/// The step reports and never fails: a person changing policy on purpose is legitimate. Paths are those under the project root
/// (which may be a subdirectory of the repository), relative to it; a rename lists the old path too.
/// </summary>
internal static class ProtectedChangesStep
{
    public const string Name = "policy.protected";

    private const int MaxFilesListed = 10;

    public static async Task<GateStep> RunAsync(GateRunContext run, CancellationToken cancellationToken)
    {
        if (run.BaseRef is null)
        {
            return new GateStep(Name, 0, Verdict.NotApplicable, 0, "not applicable without --base");
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        IReadOnlyList<string>? changed = await WorkspaceGit.ChangedNamesAsync(run.ProjectRoot, Git.SinceMergeBase(run.BaseRef), run.TimeLeft(), cancellationToken);
        if (changed is null)
        {
            return new GateStep(Name, 0, Verdict.Error, stopwatch.ElapsedMilliseconds, $"git diff against '{run.BaseRef}' failed; is the checkout deep enough (fetch-depth: 0), and does every unit repository have that ref?");
        }

        Manifest? manifest = ManifestLocator.Discover(run.ProjectRoot).Manifest;
        IReadOnlyList<string> globs = manifest is null ? DefaultProtectedPaths.Globs : DefaultProtectedPaths.For(manifest);
        string[] touched = [.. changed
            .Where(file => PathRules.FindMatch(run.ProjectRoot, Path.Combine(run.ProjectRoot, file), globs) is not null)
            .Order(StringComparer.Ordinal)];

        return new GateStep(Name, 0, Verdict.Pass, stopwatch.ElapsedMilliseconds, touched.Length == 0
            ? string.Empty
            : $"{touched.Length} protected files changed, needs human review: {string.Join(", ", touched.Take(MaxFilesListed))}{(touched.Length > MaxFilesListed ? ", …" : string.Empty)}");
    }
}
