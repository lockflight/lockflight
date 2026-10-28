using Lockflight.Cli.Config;
using Lockflight.Cli.Gates.Baselines;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>
/// Whether a phase's artifacts may be approved: an approval runs the phase check first (<c>phase check specify</c> for the spec,
/// <c>phase check tasks</c> for the tasks) and is refused while any item fails, the same way in a terminal and in a session.
/// <see cref="Explanation"/> lists the failures and how to fix them; it is empty when <see cref="Ready"/>.
/// </summary>
internal sealed record ApprovalReadiness(IReadOnlyList<PhaseCheckItem> Failures, string Explanation)
{
    private const string SpecTemplate = ".specify/templates/spec-template.md";

    public bool Ready => Failures.Count == 0;

    public static ApprovalReadiness Of(Manifest manifest, Feature feature, ApprovalPhase phase)
    {
        string checkPhase = CheckPhaseOf(phase);
        PhaseCheckItem[] failures = [.. PhaseChecks.Run(checkPhase, feature, manifest)!.Where(item => !item.Passed)];
        if (failures.Length == 0)
        {
            return new ApprovalReadiness([], string.Empty);
        }

        string phaseName = ApprovalPhaseNames.ToName(phase);
        string rerun = $"{EngineCommand.Name} phase check {checkPhase} --feature {feature.Id}";
        string fix = phase == ApprovalPhase.Spec
            ? $"Fix spec.md as each FAIL says (every user story needs a Given/When/Then acceptance scenario), or rewrite it to Spec Kit's template ({SpecTemplate})"
            : "Fix what each FAIL names: approve the spec first when it is not approved, and correct tasks.md (or regenerate it with /speckit-tasks)";
        string explanation = $"the {phaseName} of {feature.Id} does not pass the phase check an approval needs ({checkPhase}):\n"
            + string.Concat(failures.Select(item => $"  FAIL {item.Name}: {item.Detail}\n"))
            + $"{fix}; then run `{rerun}` until it passes, and approve again"
            + AdoptedNote(manifest, feature);
        return new ApprovalReadiness(failures, explanation);
    }

    private static string CheckPhaseOf(ApprovalPhase phase) => phase switch
    {
        ApprovalPhase.Spec => PhaseChecks.SpecifyPhase,
        ApprovalPhase.Tasks => PhaseChecks.TasksPhase,
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, null),
    };

    /// <summary>For a feature that predates Lockflight and has no records yet: it needs no approval, so leaving it as it is is fine.</summary>
    private static string AdoptedNote(Manifest manifest, Feature feature) =>
        new AdoptedFeatures(manifest.Root).Load().Contains(feature.Id) && !Directory.Exists(feature.RecordDirectory)
            ? $"\n{feature.Id} predates Lockflight (adopted): `approve verify --base` and `review` need no approval for it, so it may stay as it is; "
              + "approving it brings it under the lifecycle, which needs artifacts that pass the check"
            : string.Empty;
}
