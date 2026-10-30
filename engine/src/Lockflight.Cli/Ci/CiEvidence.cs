using Lockflight.Cli.Config;
using System.Globalization;
using System.Text.Json.Nodes;
using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// The CI judge's evidence, <c>lockflight.ci-evidence/v1</c>: what was examined (inputs, policy, red commits, class) and what was
/// concluded (each check, each status context, the verdict). Strings from the pull request (subjects, branch, paths) are data and
/// are capped in length.
/// </summary>
internal static class CiEvidence
{
    public const string Schema = "lockflight.ci-evidence/v1";
    public const string FileName = "evidence.json";
    public const string LocalFileName = "last.json";

    /// <summary>The longest string from the pull request kept in evidence; longer ones are cut and end with an ellipsis.</summary>
    public const int MaxTextLength = 300;

    public static JsonObject Build(JudgeRun run, string engineVersion, string runtimeIdentifier)
    {
        JudgePlan plan = run.Plan;
        JudgeInputs inputs = plan.Inputs;
        return new JsonObject
        {
            ["schema"] = Schema,
            ["engine"] = new JsonObject { ["version"] = engineVersion, ["rid"] = runtimeIdentifier, ["sha256"] = null },
            ["platform"] = JudgePlatformNames.ToName(inputs.Platform),
            ["event"] = JudgeEventNames.ToName(inputs.Event),
            ["run"] = null,
            ["head"] = inputs.Head,
            ["base"] = new JsonObject
            {
                ["sha"] = inputs.Base,
                ["mergeBase"] = plan.MergeBase,
                ["otherMergeBases"] = Strings(plan.OtherMergeBases),
            },
            ["branch"] = inputs.Branch is string branch ? Cap(branch) : null,
            ["policy"] = new JsonObject
            {
                ["from"] = plan.Policy.Commit,
                ["manifestSha256"] = plan.Policy.ManifestSha256,
                ["restored"] = Strings(plan.Policy.Files),
            },
            ["ignored"] = Strings(plan.Ignored.Select(Cap)),
            ["feature"] = null,
            ["changeClass"] = ChangeClassNode(plan.Class.Classification, engineVersion),
            ["redCommits"] = new JsonArray([.. plan.Reds.All.Select(RedCommitNode)]),
            ["cycles"] = new JsonArray([.. (run.Findings.Cycles ?? []).Select(cycle => CycleNode(cycle, run.Findings))]),
            ["existingChanged"] = run.Findings.ExistingChanged is IReadOnlyList<ExistingChangedTest> existing
                ? new JsonArray([.. existing.Select(test => new JsonObject { ["key"] = Cap(test.Key), ["state"] = ExistingChangeNames.ToName(test.State), ["why"] = Cap(test.Why) })])
                : null,
            ["testsWithoutRed"] = Strings((run.Findings.Green?.WithoutRed ?? []).Select(Cap)),
            ["green"] = run.Findings.Green is GreenJudgement green ? GreenNode(green) : null,
            ["trace"] = null,
            ["gates"] = null,
            ["approvals"] = null,
            ["blocked"] = null,
            ["review"] = new JsonObject { ["state"] = inputs.ReviewExpected ? "expected" : "not-expected", ["artifact"] = inputs.ReviewExpected ? "lockflight-review" : null },
            ["checks"] = new JsonArray([.. run.Checks.Select(CheckNode)]),
            ["contexts"] = new JsonObject
            {
                [StatusContextNames.ToName(StatusContext.Gates)] = CheckVerdictNames.ToName(run.VerdictOf(StatusContext.Gates)),
                [StatusContextNames.ToName(StatusContext.Judge)] = CheckVerdictNames.ToName(run.VerdictOf(StatusContext.Judge)),
            },
            ["verdict"] = CheckVerdictNames.ToName(run.Verdict),
            ["budget"] = new JsonObject
            {
                ["limitSeconds"] = plan.Policy.Ci.TimeoutMinutes * 60,
                ["usedSeconds"] = (int)(run.FinishedAt - run.StartedAt).TotalSeconds,
                ["exhausted"] = false,
                ["notRun"] = new JsonArray(),
            },
            ["builds"] = new JsonArray([.. run.Findings.Builds.Select(build => new JsonObject
            {
                ["commit"] = build.Commit,
                ["purpose"] = build.Purpose,
                ["seconds"] = Math.Round(build.Seconds, 1),
                ["outcome"] = TreeOutcomeNames.ToName(build.Outcome),
            })]),
            ["startedAt"] = Timestamp(run.StartedAt),
            ["finishedAt"] = Timestamp(run.FinishedAt),
        };
    }

    /// <summary>The plan as <c>lockflight ci --plan</c> prints it.</summary>
    public static JsonObject Plan(JudgePlan plan) => new()
    {
        ["head"] = plan.Inputs.Head,
        ["base"] = plan.Inputs.Base,
        ["mergeBase"] = plan.MergeBase,
        ["policy"] = plan.Policy.Commit,
        ["changeClass"] = ChangeClassNames.ToName(plan.Class.Classification.Class),
        ["redCommits"] = new JsonArray([.. plan.Reds.All.Select(RedCommitNode)]),
        ["checks"] = new JsonObject([.. plan.Levels.OrderBy(level => level.Key).Select(level =>
            KeyValuePair.Create(CheckIdNames.ToName(level.Key), (JsonNode?)CheckLevelNames.ToName(level.Value)))]),
        ["steps"] = new JsonArray([.. plan.Steps.Select(step => new JsonObject
        {
            ["id"] = step.Id,
            ["commit"] = step.Commit,
            ["purpose"] = step.Purpose,
            ["work"] = step.Work,
            ["builds"] = step.Builds,
        })]),
        ["builds"] = plan.Builds,
    };

    /// <summary>Cuts a string from the pull request to <see cref="MaxTextLength"/> characters.</summary>
    public static string Cap(string text) => text.Length <= MaxTextLength ? text : text[..(MaxTextLength - 1)] + "…";

    private static JsonObject ChangeClassNode(Classification classification, string engineVersion) => new()
    {
        ["class"] = ChangeClassNames.ToName(classification.Class),
        ["computed"] = ChangeClassNames.ToName(classification.Computed),
        ["policy"] = new JsonObject
        {
            ["base"] = classification.Policy.BaseRef,
            ["commit"] = classification.Policy.Commit,
            ["manifest"] = classification.Policy.ManifestBlob,
            ["preset"] = classification.Policy.Preset,
        },
        ["signals"] = new JsonArray([.. classification.Signals.Select(signal => new JsonObject
        {
            ["id"] = signal.Id,
            ["raisesTo"] = ChangeClassNames.ToName(signal.RaisesTo),
            ["strength"] = SignalStrengthNames.ToName(signal.Strength),
            ["where"] = Cap(signal.Where),
            ["detail"] = signal.Detail,
        })]),
        ["raises"] = new JsonArray([.. classification.Raises.Select(raise => new JsonObject { ["source"] = raise.Source, ["to"] = ChangeClassNames.ToName(raise.To) })]),
        ["engine"] = engineVersion,
    };

    private static JsonObject GreenNode(GreenJudgement green) => new()
    {
        ["verdict"] = CheckVerdictNames.ToName(green.Verdict),
        ["total"] = green.Total,
        ["failed"] = green.Failed,
        ["problems"] = Strings(green.Problems.Select(Cap)),
        ["flaky"] = Strings(green.Flaky.Select(Cap)),
    };

    private static JsonObject CycleNode(CycleJudgement cycle, JudgeFindings findings) => new()
    {
        ["label"] = cycle.Red.Label,
        ["commit"] = cycle.Red.Commit,
        ["build"] = TreeOutcomeNames.ToName(cycle.Build),
        ["verdict"] = CheckVerdictNames.ToName(cycle.Verdict),
        ["why"] = Cap(cycle.Why),
        ["tests"] = new JsonArray([.. cycle.Tests.Select(test => new JsonObject
        {
            ["key"] = Cap(test.Key),
            ["requirements"] = Strings(test.Requirements.Select(Cap)),
            ["red"] = RedClassNames.ToName(test.RedClass),
            ["why"] = cycle.Verdicts.TryGetValue(test.Key, out RedVerdict? verdict) ? Cap(verdict.Why) : null,
            ["assertionFree"] = test.AssertionFree,
            ["rows"] = test.Rows,
            ["fingerprintAtRed"] = cycle.FingerprintsAtRed.GetValueOrDefault(test.Key),
            ["fingerprintAtHead"] = findings.HeadFingerprints.GetValueOrDefault(test.Key),
            ["sinceRed"] = findings.SinceRed?.FirstOrDefault(since => since.Key == test.Key) is SinceRedTest since ? SinceRedNames.ToName(since.State) : null,
            ["amend"] = findings.SinceRed?.FirstOrDefault(since => since.Key == test.Key && since.State != SinceRed.Unchanged && since.State != SinceRed.MissingAtHead) is SinceRedTest amend
                ? new JsonObject
                {
                    ["proofPoint"] = findings.HeadCommit,
                    ["files"] = Strings(amend.ChangedFiles.Select(Cap)),
                    ["summary"] = Cap(amend.Why),
                    ["red"] = amend.Recheck is RedClass recheck ? RedClassNames.ToName(recheck) : null,
                }
                : null,
            ["atHead"] = findings.Green?.AtHead.GetValueOrDefault(test.Key),
        })]),
    };

    private static JsonObject RedCommitNode(RedCommit red) => new()
    {
        ["commit"] = red.Commit,
        ["label"] = red.Label,
        ["forkPoint"] = red.ForkPoint,
        ["superseded"] = red.Superseded,
        ["malformed"] = red.Malformed is string malformed ? Cap(malformed) : null,
        ["subject"] = Cap(red.Subject),
    };

    private static JsonObject CheckNode(CheckResult check) => new()
    {
        ["id"] = CheckIdNames.ToName(check.Id),
        ["context"] = StatusContextNames.ToName(check.Context),
        ["verdict"] = CheckVerdictNames.ToName(check.Verdict),
        ["requirement"] = CheckLevelNames.ToName(check.Level),
        ["why"] = Cap(check.Why),
    };

    private static JsonArray Strings(IEnumerable<string> values) => new([.. values.Select(value => (JsonNode?)value)]);

    private static string Timestamp(DateTimeOffset at) => at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
