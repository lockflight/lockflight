using Lockflight.Cli.ApprovalsOfRecord;
using Lockflight.Cli.Config;
using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Paths;
using Lockflight.Cli.Routing;
using Lockflight.Cli.Workspace;
using Requirement = Lockflight.Cli.Lifecycle.Requirement;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Whether a pull request is judged as a structural-only change: one whose spec is all <c>[structural]</c> (a behaviour-preserving
/// refactoring, an architecture rule) needs no red commit when a person gates that spec. Decided from git alone, in the planner, so
/// the report job decides it again before it trusts the judge's evidence. Eligible, red and tests since red are off and trace and
/// the existing tests are required instead, which the build then has to earn: every requirement covered by a new structural test
/// that passes on the fork point and at head, no new test that is not structural, and the base's version of every existing test
/// the pull request changed passing on the head's code. The markers are written by whoever writes the spec, an agent included:
/// what holds the claim "this is structural" is the person who reviews the spec.
/// </summary>
internal static class StructuralLane
{
    private const string DatabaseSignals = "db.";

    private const string SingleSource = "single";

    /// <summary>Routing signals that carry behaviour a structural test does not pin; any of them refuses the lane.</summary>
    private static readonly HashSet<string> BehaviourSignals = new(StringComparer.Ordinal)
    {
        SignalIds.ConfigChanged, SignalIds.DepsChanged, SignalIds.ApiContractFile, SignalIds.ApiPublicApiFile, SignalIds.BinaryChanged,
        SignalIds.CriticalPolicy, SignalIds.CiWorkflowChanged,
    };

    /// <summary>
    /// Reads what <see cref="Decide"/> needs from git for a planned pull request whose red check is on; null when the pull request
    /// has no spec in the head's tree, or one that is not all <c>[structural]</c>.
    /// </summary>
    /// <param name="shell">The workspace shell's policy when the repository is a unit of one; null otherwise.</param>
    public static async Task<StructuralEligibility?> ReadAsync(JudgePlan plan, ShellPolicy? shell, CancellationToken cancellationToken)
    {
        SpecResolution spec = await TreeSpecSource.ResolveAsync(plan, plan.Inputs.Branch, [], cancellationToken);
        if (spec.SpecText is not string text || spec.Problem is not null)
        {
            return null;
        }

        SpecDocument document = SpecDocument.Parse(text);
        if (!AllStructural(document))
        {
            return null;
        }

        ApprovalSettings settings = plan.Policy.Manifest?.Approvals ?? ApprovalSettings.ForVersion1;
        ApprovalsFacts? approvals = plan.Inputs.Base is string baseCommit
            ? await ApprovalsChecker.CheckAsync(
                new ApprovalsRequest(plan.Inputs.Root, baseCommit, plan.Inputs.Head, settings, plan.Class.Classification.Class,
                    spec.Source == SingleSource ? null : spec.FeatureId, plan.ChangedFiles, ApprovalsJudge.Codeowners(plan.Inputs.Platform)),
                cancellationToken)
            : null;
        StructuralLaneFacts facts = new(
            PullRequest: plan.Inputs.Event == JudgeEvent.PullRequest && plan.Inputs.Base is not null,
            Routed: plan.Class.Source == InterimClassification.RoutingSource,
            Unit: shell is not null || plan.Policy.Manifest?.Shell is not null,
            plan.Reds.All.Count,
            spec,
            document.Requirements,
            approvals,
            settings.SpecPaths,
            plan.ChangedFiles,
            plan.Class.Classification.Signals);
        return Decide(facts);
    }

    /// <summary>
    /// The lane's eligibility from the facts; null when there is no spec, or its requirements are not all <c>[structural]</c> (a
    /// mixed spec never qualifies, so a branch named after a feature with one structural requirement gets nothing).
    /// </summary>
    public static StructuralEligibility? Decide(StructuralLaneFacts facts)
    {
        if (facts.Spec is not { FeatureId: string featureId, SpecPath: string specPath, Problem: null } || !AllStructural(facts.Requirements))
        {
            return null;
        }

        List<StructuralUnmet> unmet = [];
        if (!facts.PullRequest || !facts.Routed || facts.Unit)
        {
            unmet.Add(new StructuralUnmet(StructuralCondition.Routed, facts.Unit
                ? "a unit repository of a workspace shell is judged against the shell's spec, so its structural requirements are not checked here"
                : "the change class was not computed from the target branch's routing policy"));
        }

        if (facts.RedCommits > 0)
        {
            unmet.Add(new StructuralUnmet(StructuralCondition.NoRedCommit, "a commit carries a Lockflight-Red trailer (well-formed or not), so the pull request is judged by red commits"));
        }

        StructuralRoute? route = Route(facts, specPath);
        if (route is null)
        {
            unmet.Add(new StructuralUnmet(StructuralCondition.SpecReviewed, NotReviewed(facts, specPath)));
        }

        ChangeSignal[] behaviour = [.. facts.Signals.Where(signal => CarriesBehaviour(signal.Id))];
        if (behaviour.Length > 0)
        {
            unmet.Add(new StructuralUnmet(StructuralCondition.NoBehaviourSignal,
                $"the change carries behaviour no structural test pins: {string.Join(", ", behaviour.Take(3).Select(signal => $"{signal.Id} in {signal.Where}"))}"
                + (behaviour.Length > 3 ? $" and {behaviour.Length - 3} more" : string.Empty)));
        }

        return new StructuralEligibility(unmet, route, specPath, featureId, [.. facts.Requirements.Select(requirement => requirement.Id).Distinct(StringComparer.Ordinal)]);
    }

    /// <summary>Whether a routing signal carries behaviour a structural test does not pin.</summary>
    public static bool CarriesBehaviour(string signalId) =>
        BehaviourSignals.Contains(signalId) || signalId.StartsWith(DatabaseSignals, StringComparison.Ordinal);

    private static bool AllStructural(SpecDocument document) =>
        document.DuplicateMarkedIds.Count == 0 && AllStructural(document.Requirements);

    private static bool AllStructural(IReadOnlyList<Requirement> requirements) =>
        requirements.Count > 0 && requirements.All(requirement => requirement.Structural);

    /// <summary>
    /// How a person gated the spec: changed in this pull request where the base's CODEOWNERS gives its path an owner (the platform's
    /// required code-owner review then covers it), or merged first and unchanged where spec-first applies; null when neither.
    /// </summary>
    private static StructuralRoute? Route(StructuralLaneFacts facts, string specPath)
    {
        if (facts.Approvals is not ApprovalsFacts approvals)
        {
            return null;
        }

        CodeownersCoverage codeowners = approvals.Codeowners;
        if (facts.ChangedFiles.Contains(specPath, StringComparer.Ordinal) && codeowners is { File: not null, GitError: null }
            && facts.SpecPaths.Any(glob => PathRules.Matches(glob, specPath) && codeowners.Covers.Contains(glob, StringComparer.Ordinal)))
        {
            return StructuralRoute.Codeowners;
        }

        return approvals.SpecFirst is { Applies: true, Satisfied: true } specFirst && specFirst.SpecPath == specPath ? StructuralRoute.SpecFirst : null;
    }

    private static string NotReviewed(StructuralLaneFacts facts, string specPath)
    {
        const string Remedy = "or commit a red for the behaviour";
        if (facts.Approvals is not ApprovalsFacts approvals)
        {
            return $"a structural-only change needs its spec reviewed, and the approval facts of {specPath} could not be read; {Remedy}";
        }

        if (approvals.SpecFirst.Applies)
        {
            return $"a structural-only change needs its spec reviewed: spec-first applies, and {specPath} was not merged on the target branch first, unchanged here; {Remedy}";
        }

        if (!facts.ChangedFiles.Contains(specPath, StringComparer.Ordinal))
        {
            return $"a structural-only change needs its spec reviewed: {specPath} is not changed in this pull request and spec-first does not apply; change it here under CODEOWNERS, {Remedy}";
        }

        CodeownersCoverage codeowners = approvals.Codeowners;
        return codeowners switch
        {
            { GitError: string gitError } => $"a structural-only change needs its spec reviewed, and its CODEOWNERS coverage could not be read ({gitError}); {Remedy}",
            { File: null } => $"a structural-only change needs its spec reviewed: the target branch has no CODEOWNERS file this platform reads that gives {specPath} an owner; add one, {Remedy}",
            _ => $"a structural-only change needs its spec reviewed: the target branch's {codeowners.File} gives {specPath} no owner under approvals.specPaths; add one, {Remedy}",
        };
    }
}
