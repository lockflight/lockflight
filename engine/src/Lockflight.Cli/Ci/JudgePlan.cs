using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// What one judge run will examine, worked out from git alone before any build: the inputs, the base policy, the merge base (and
/// any others of a criss-cross history), the red commits, the change's class and the level of every check, the steps, the files the
/// pull request changed, and the tree's and environment's inputs the judge ignores on purpose.
/// </summary>
internal sealed record JudgePlan(
    JudgeInputs Inputs,
    BasePolicy Policy,
    string? MergeBase,
    IReadOnlyList<string> OtherMergeBases,
    RedCommitSelection Reds,
    InterimClassification Class,
    RequirementSet Requirements,
    IReadOnlyDictionary<CheckId, CheckLevel> Levels,
    IReadOnlyList<PlanStep> Steps,
    IReadOnlyList<string> ChangedFiles,
    IReadOnlyList<string> Ignored)
{
    public int Builds => Steps.Sum(step => step.Builds);
}
