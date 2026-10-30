using Lockflight.Cli.Config;
using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Builds and lists the trees of a pull request one after the other in the judge's single worktree, each with the base policy laid
/// over it. A build that fails incrementally is retried once from clean before it counts as failed. Every build is recorded.
/// </summary>
internal sealed class TreeBuilder(JudgeWorktree worktree, BasePolicy policy, string repositoryRoot, TimeProvider time)
{
    private const int DefaultTimeoutSeconds = 600;

    private readonly List<BuildRecord> builds = [];
    private readonly Dictionary<string, TreeListing> listed = new(StringComparer.Ordinal);

    public IReadOnlyList<BuildRecord> Builds => builds;

    /// <summary>The root of the tree being built: production projects are found under it.</summary>
    public string Root => worktree.Root;

    public TimeSpan ProcessLimit => TimeSpan.FromSeconds(policy.Manifest?.Gates.TimeoutSeconds ?? DefaultTimeoutSeconds);

    /// <summary>
    /// The tree at <paramref name="commit"/>, built and listed. A commit listed before is not built again unless
    /// <paramref name="fresh"/> asks for its build to be in the worktree (to run its tests) and the worktree has moved on since.
    /// </summary>
    public async Task<TreeListing> ListAsync(string commit, string purpose, bool fresh, CancellationToken cancellationToken)
    {
        if (listed.TryGetValue(commit, out TreeListing? known) && (!fresh || worktree.Commit == commit))
        {
            return known;
        }

        TestWorkspace workspace = Workspace();
        long started = time.GetTimestamp();
        TreeListing listing = await BuildAndListAsync(commit, workspace, cancellationToken);
        builds.Add(new BuildRecord(commit, purpose, time.GetElapsedTime(started).TotalSeconds, listing.Outcome));
        listed[commit] = listing;
        return listing;
    }

    /// <summary>
    /// The tree at <paramref name="commit"/> with <paramref name="paths"/> from <paramref name="overlayCommit"/> laid over it (and
    /// <paramref name="deleted"/> removed), built and listed; never cached, since no commit holds this tree.
    /// </summary>
    public async Task<TreeListing> ListOverlaidAsync(
        string commit, string overlayCommit, IReadOnlyList<string> paths, IReadOnlyList<string> deleted, string purpose, CancellationToken cancellationToken)
    {
        TestWorkspace workspace = Workspace();
        long started = time.GetTimestamp();
        TreeListing listing = await BuildAndListAsync(commit, workspace, cancellationToken, async () => await worktree.OverlayAsync(overlayCommit, paths, deleted, policy.Commit, cancellationToken));
        builds.Add(new BuildRecord(commit, purpose, time.GetElapsedTime(started).TotalSeconds, listing.Outcome));
        listed.Remove(commit);
        return listing;
    }

    private async Task<TreeListing> BuildAndListAsync(string commit, TestWorkspace workspace, CancellationToken cancellationToken, Func<Task<string?>>? overlay = null)
    {
        string? buildOutput = null;
        foreach (bool clean in new[] { false, true })
        {
            string? notReady = await worktree.MoveToAsync(commit, policy.Commit, clean, cancellationToken);
            if (notReady is null && overlay is not null)
            {
                notReady = await overlay();
            }

            if (notReady is not null)
            {
                return new TreeListing(commit, TreeOutcome.Broken, workspace, null, notReady);
            }

            if (!Directory.EnumerateFiles(worktree.Root, "*.*proj", SearchOption.AllDirectories).Any())
            {
                return new TreeListing(commit, TreeOutcome.NoTestApps, workspace, null, "no .NET project in the tree; red/green needs an xUnit or NUnit test project");
            }

            buildOutput = await workspace.BuildAsync(cancellationToken);
            if (buildOutput is null)
            {
                break;
            }
        }

        if (buildOutput is not null)
        {
            return new TreeListing(commit, TreeOutcome.CompileFailed, workspace, null, RedGreenSession.Tail(buildOutput));
        }

        if (workspace.DiscoveryProblem is string incomplete)
        {
            return new TreeListing(commit, TreeOutcome.DiscoveryIncomplete, workspace, null, incomplete);
        }

        if (workspace.Apps.Count == 0)
        {
            return new TreeListing(commit, TreeOutcome.NoTestApps, workspace, null, "no xUnit test project (v3, or v2 on VSTest) found; other frameworks get the gates only");
        }

        return await workspace.ListAsync(cancellationToken) is IReadOnlyList<TestCase> inventory
            ? new TreeListing(commit, TreeOutcome.Ok, workspace, inventory, null) { Fingerprints = TestFingerprints.Compute(workspace, inventory, worktree.Root) }
            : new TreeListing(commit, TreeOutcome.Broken, workspace, null, $"the test runner could not list the tests of {workspace.FailedApp}");
    }

    /// <summary>The base manifest's build targets, rooted in the worktree; the repository root when the base has no manifest.</summary>
    private TestWorkspace Workspace() => policy.Manifest is Manifest manifest
        ? TestWorkspaces.For(manifest, feature: null, time).MovedTo(worktree.Root)
        : new TestWorkspace(repositoryRoot, [new BuildTarget(repositoryRoot, BuildFile: null, PreferredFramework: null)], ProcessLimit, time).MovedTo(worktree.Root);
}
