using Lockflight.Cli.Config;
using Lockflight.Cli.Workspace;

namespace Lockflight.Cli.Ci;

/// <summary>
/// The spec of a unit repository's pull request, from the read-only clone of its workspace shell: the feature is the one the branch
/// names among the shell's <c>specs/</c> at the clone's commit (the shell's branch, so a spec there is merged), and its spec is read
/// there. A branch named like a feature (<c>NNN-slug</c>) whose spec the shell does not hold yet cannot be vouched for: the unit's
/// pull request waits until the shell's spec pull request merges. The shell holds the specs of all its units, so its only feature
/// is no evidence that a pull request belongs to it, and the unit never changes a shell spec.
/// </summary>
internal sealed class ShellSpecSource : ISpecSource
{
    private const string Specs = "specs/";

    public async Task<SpecResolution> ResolveAsync(JudgePlan plan, string? branch, string? shellPath, CancellationToken cancellationToken) =>
        plan.Shell is ShellPolicy shell
            ? await ResolveAsync(plan, shell, branch, [], cancellationToken)
            : SpecResolution.None("the workspace shell's clone was not read");

    /// <summary>As the interface, with the requirements the accepted cycle tests name (they make a missing spec suspicious).</summary>
    public static async Task<SpecResolution> ResolveAsync(JudgePlan plan, ShellPolicy shell, string? branch, IReadOnlyList<string> testRequirements, CancellationToken cancellationToken)
    {
        string url = plan.Policy.Manifest?.Shell?.Url ?? shell.Directory;
        string[] features = await TreeSpecSource.FeaturesAsync(shell.Directory, shell.Commit, cancellationToken);
        if (TreeSpecSource.ByBranch(branch, features) is not string id)
        {
            if (TreeSpecSource.FeatureShaped(branch) is string pending)
            {
                return InShell(new SpecResolution(pending, "branch", $"{Specs}{pending}/spec.md", null, null, SpecOrigin.Shell,
                    $"the workspace shell {url} holds no {Specs}{pending}/ at {CommitIds.Short(shell.Commit)} yet: merge the feature's spec pull request in the shell first, then run this check again"), url, shell);
            }

            bool suspicious = features.Length > 0
                && (testRequirements.Count > 0 || TreeSpecSource.NamedBySubjects(plan, features).Length > 0 || plan.Class.Classification.Class >= ChangeClass.Feature);
            return InShell(SpecResolution.None(suspicious
                ? $"no spec of the workspace shell {url} was found for this pull request, but its tests name requirements, a red commit names a feature, or its class needs a spec; name the branch after the shell's feature (specs/<id>/)"
                : null), url, shell);
        }

        if (TreeSpecSource.NamedBySubjects(plan, features).FirstOrDefault(other => other != id) is string disagreeing)
        {
            return InShell(new SpecResolution(id, "branch", null, null, null, SpecOrigin.Shell, $"the branch and the tests disagree on the feature: {id} by the branch, {disagreeing} by a red commit's subject"), url, shell);
        }

        string specPath = $"{Specs}{id}/spec.md";
        string? spec = await JudgeGit.ShowFileAsync(shell.Directory, shell.Commit, specPath, cancellationToken);
        string? tasks = await JudgeGit.ShowFileAsync(shell.Directory, shell.Commit, $"{Specs}{id}/tasks.md", cancellationToken);
        return InShell(new SpecResolution(id, "branch", specPath, spec, tasks, SpecOrigin.Shell,
            spec is null ? $"the workspace shell {url} has no {specPath} at {CommitIds.Short(shell.Commit)}: merge the spec in the shell first" : null), url, shell);
    }

    private static SpecResolution InShell(SpecResolution resolution, string url, ShellPolicy shell) =>
        resolution with { Origin = SpecOrigin.Shell, ShellUrl = url, ShellCommit = shell.Commit };
}
