using Lockflight.Cli.Ci;

namespace Lockflight.Cli.Lifecycle.Fix;

/// <summary>
/// The candidate red as judged: the judgement, and the tree it was built and listed in (its test applications, to reproduce and
/// cross-check the tests); or why no worktree could be opened (<see cref="Problem"/>, with the exit code to end on).
/// </summary>
internal sealed record RedCheckResult(CycleJudgement? Cycle, TreeListing? Tree, string? Problem)
{
    public static RedCheckResult Judged(CycleJudgement cycle, TreeListing? tree) => new(cycle, tree, null);

    public static RedCheckResult Failed(string problem) => new(null, null, problem);
}
