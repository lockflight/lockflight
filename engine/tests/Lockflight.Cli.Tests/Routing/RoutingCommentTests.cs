using System.Text;
using Lockflight.Cli.Config;
using Lockflight.Cli.Routing;
using Lockflight.Cli.Tests.Goldens;

namespace Lockflight.Cli.Tests.Routing;

public sealed class RoutingCommentTests
{
    private static readonly string[] Shown =
    [
        "fix-body-edit", "behaviour-size-lines", "feature-endpoint-attribute", "feature-grpc-service", "critical-move-out", "critical-policy-manifest",
    ];

    [Fact]
    public async Task TheCommentLinesOfAFewFixturesAreAsTheGoldenSays()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        StringBuilder text = new();
        foreach (string name in Shown)
        {
            (ChangeSet changeSet, Manifest? manifest) = await ChangeClassifierTests.ReadAsync(name, extraFile: null, cancellationToken);
            RoutingPolicy policy = ChangeClassifierTests.Policy(manifest);
            Classification classification = ChangeClassifier.Classify(changeSet, policy, []);
            text.Append($"## {name}\n{RoutingComment.ClassLine(classification, policy)}\n{RoutingComment.NeedsLine(ClassRequirements.For(classification.Class, policy))}\n");
        }

        GoldenFiles.Verify("routing-comment", text.ToString());
    }

    [Fact]
    public void RaisesAreNamedAndALowerOneIsShownAsIgnored()
    {
        RoutingPolicy policy = ChangeClassifierTests.Policy(manifest: null);
        Classification classification = new(
            ChangeClass.Critical,
            ChangeClass.Feature,
            [new ChangeSignal("db.migration", ChangeClass.Feature, SignalStrength.Heuristic, "src/Migrations/1.cs", "path")],
            [new ClassRaise("label lockflight:class/critical", ChangeClass.Critical), new ClassRaise("spec 004-refunds", ChangeClass.Fix)],
            [],
            policy.Source);

        Assert.Equal(
            "**Class: critical** · computed feature (floor fix, preset default) · raised by label lockflight:class/critical · spec 004-refunds says fix, ignored (lower than computed) · db.migration `src/Migrations/1.cs`",
            RoutingComment.ClassLine(classification, policy));
    }

    [Fact]
    public void ALongListStaysWithinTheLineLimitAndCountsTheRest()
    {
        RoutingPolicy policy = ChangeClassifierTests.Policy(manifest: null);
        ChangeSignal[] signals = [.. Enumerable.Range(0, 20).Select(index =>
            new ChangeSignal("critical.path", ChangeClass.Critical, SignalStrength.Boundary, $"src/{new string('a', 50)}/{new string('b', 50)}/File{index}.cs", "glob"))];
        Classification classification = new(ChangeClass.Critical, ChangeClass.Critical, signals, [], [], policy.Source);

        string line = RoutingComment.ClassLine(classification, policy);

        Assert.True(line.Length <= RoutingComment.MaxLineLength + 12, line);
        Assert.Contains("critical.path (policy) `…/", line, StringComparison.Ordinal);
        Assert.EndsWith(" more", line, StringComparison.Ordinal);
    }
}
