using System.Text.Json.Nodes;
using Lockflight.Cli.Ci;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Routing;
using Lockflight.Cli.Tests.Goldens;

namespace Lockflight.Cli.Tests.Ci;

public sealed class CiEvidenceTests
{
    private static readonly string Head = new('a', 40);
    private static readonly string Base = new('b', 40);
    private static readonly string MergeBase = new('c', 40);
    private static readonly string Red = new('d', 40);
    private static readonly string EngineSha256 = new('9', 64);
    private static readonly DateTimeOffset StartedAt = new(2026, 10, 29, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheEvidenceOfARunIsPinned() =>
        GoldenFiles.Verify("ci-evidence", JsonFiles.Format(CiEvidence.Build(Run(), "0.3.0", "linux-x64", EngineSha256)));

    /// <summary>The evidence of <see cref="Run"/>, as the engine writes it.</summary>
    internal static JsonObject Evidence() => CiEvidence.Build(Run(), "0.3.0", "linux-x64", EngineSha256);

    [Fact]
    public void ThePlanIsPinned() => GoldenFiles.Verify("ci-plan", JsonFiles.Format(CiEvidence.Plan(Run().Plan)));

    [Fact]
    public void StringsFromThePullRequestAreCapped()
    {
        string capped = CiEvidence.Cap(new string('x', 1000));

        Assert.Equal(CiEvidence.MaxTextLength, capped.Length);
        Assert.EndsWith("…", capped, StringComparison.Ordinal);
    }

    /// <summary>A pull request run with red commits of every kind and nothing built.</summary>
    internal static JudgeRun Run()
    {
        JudgeInputs inputs = new("/repo", Head, Base, "012-checkout", JudgePlatform.GitHub, JudgeEvent.PullRequest, "/tmp/evidence", ShellPath: null, ReviewExpected: true);
        BasePolicy policy = new(Base, Manifest: null, ManifestSha256: null, ManifestBlob: null, ["lockflight.yaml"]);
        RedCommit[] reds =
        [
            new RedCommit(new string('e', 40), "test(012-checkout): red for US1", "2026-10-29T09:00:00+00:00", "1", Malformed: null, Superseded: true),
            new RedCommit(new string('f', 40), "test: bad", "2026-10-29T09:10:00+00:00", null, "'x' is not a cycle number (a positive whole number such as 2)", Superseded: false),
            new RedCommit(Red, "test(012-checkout): red for US1 again", "2026-10-29T09:20:00+00:00", "1", Malformed: null, Superseded: false) { ForkPoint = MergeBase },
        ];
        RedCommitSelection selection = new(reds, [reds[2]], [], OverCap: false, LabelsOutOfOrder: false);
        InterimClassification classification = InterimClassifier.Classify(
            ["src/Shop/Price.cs"], CiSettings.DefaultLowRiskPaths, new HashSet<string>(), new HashSet<string>(), string.Empty, new PolicySource(Base, Base, null, "interim"));
        RequirementSet requirements = InterimRequirements.For(classification.Classification.Class, classification.Shape);
        IReadOnlyDictionary<CheckId, CheckLevel> levels = CheckLevels.For(requirements, classification.Shape, JudgeEvent.PullRequest);
        JudgePlan plan = new(inputs, policy, MergeBase, [], selection, classification, requirements, levels,
            [new PlanStep("P0", null, "inputs", "git only", 0), new PlanStep("P3", Red, "red 1", "build, list, run", 1)], ["src/Shop/Price.cs"], [".specify/feature.json"]);
        return new JudgeRun(plan, JudgeFindings.None, JudgeChecks.Decide(plan, JudgeFindings.None), StartedAt, StartedAt.AddSeconds(12));
    }
}
