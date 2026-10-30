using Lockflight.Cli.Routing;
using Lockflight.Cli.Workspace;

namespace Lockflight.Cli.Ci;

/// <summary>
/// What one judge run will examine, worked out from git alone before any build: the inputs, the base policy, the merge base (and
/// any others of a criss-cross history), the red commits, the change's class and the level of every check, the steps, the files the
/// pull request changed, the tree's and environment's inputs the judge ignores on purpose, and, in a unit repository of a
/// workspace shell, the shell's policy for it.
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

    /// <summary>The workspace shell's policy for this unit repository, read from its clone; null when the repository is no unit.</summary>
    public ShellPolicy? Shell { get; init; }
}
