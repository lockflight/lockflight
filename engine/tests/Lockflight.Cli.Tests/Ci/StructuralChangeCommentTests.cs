using System.Text.Json.Nodes;
using Lockflight.Cli.Ci;

namespace Lockflight.Cli.Tests.Ci;

/// <summary>
/// The comment of a structural-only change says what stood in for the red commit and what is accepted by review; evidence with or
/// without the section keeps the frozen shape.
/// </summary>
public sealed class StructuralChangeCommentTests
{
    [Fact]
    public void AStructuralChangeOpensWithWhatStoodInForTheRed()
    {
        string comment = CommentRenderer.Render(Verdict(Eligible(), redLevel: "off"), CommentRenderer.MaxCommentLength);

        Assert.Contains("**Structural-only change — no red commit required.** Spec `specs/012-pricing/spec.md`: 2 structural requirements; 2 new structural tests pass on the fork point and at head (1 architecture rule the change establishes).", comment, StringComparison.Ordinal);
        Assert.Contains("3 existing tests changed; the old version of 3 passes on the new code.", comment, StringComparison.Ordinal);
        Assert.Contains("under the target branch's CODEOWNERS", comment, StringComparison.Ordinal);
        Assert.Contains("Changed lines not measured (no coverage collector).", comment, StringComparison.Ordinal);
        Assert.Contains("**Accepted by review, not proved:**", comment, StringComparison.Ordinal);
    }

    [Fact]
    public void AStructuralChangeMergedSpecFirstSaysSoAndMeasuredCoverageToo()
    {
        JsonObject structural = Eligible();
        structural["route"] = "spec-first";
        structural["coverage"] = "measured";

        string comment = CommentRenderer.Render(Verdict(structural, redLevel: "off"), CommentRenderer.MaxCommentLength);

        Assert.Contains("(spec-first)", comment, StringComparison.Ordinal);
        Assert.Contains("Changed lines' coverage measured", comment, StringComparison.Ordinal);
    }

    [Fact]
    public void EvidenceWhoseRedIsOnGetsNoStructuralHeadline() =>
        Assert.DoesNotContain("Structural-only change", CommentRenderer.Render(Verdict(Eligible(), redLevel: "required"), CommentRenderer.MaxCommentLength), StringComparison.Ordinal);

    [Fact]
    public void AnAllStructuralSpecThatMissesAConditionListsIt()
    {
        JsonObject structural = Eligible();
        structural["eligible"] = false;
        structural["unmet"] = new JsonArray(new JsonObject { ["condition"] = "spec-reviewed", ["why"] = "the spec is not changed in this pull request" });

        string comment = CommentRenderer.Render(Verdict(structural, redLevel: "required"), CommentRenderer.MaxCommentLength);

        Assert.Contains("**Not a structural-only change:**", comment, StringComparison.Ordinal);
        Assert.Contains("> - the spec is not changed in this pull request", comment, StringComparison.Ordinal);
    }

    [Fact]
    public void EvidenceWithOrWithoutTheSectionKeepsTheShape()
    {
        Assert.Null(EvidenceShape.Problem(Verdict(Eligible(), redLevel: "off")));
        Assert.Null(EvidenceShape.Problem(Verdict(null, redLevel: "required")));
        JsonObject broken = Verdict(null, redLevel: "required");
        broken["structuralChange"] = "eligible";

        Assert.Equal("'structuralChange' must be an object or null", EvidenceShape.Problem(broken));
    }

    private static JsonObject Eligible() => new()
    {
        ["eligible"] = true,
        ["unmet"] = new JsonArray(),
        ["route"] = "codeowners",
        ["spec"] = "specs/012-pricing/spec.md",
        ["requirements"] = new JsonArray("FR-010", "FR-011"),
        ["tests"] = new JsonArray(
            new JsonObject { ["key"] = "Shop.Tests:Shop.Tests.A.B", ["outcome"] = "preserved" },
            new JsonObject { ["key"] = "Shop.Tests:Shop.Tests.A.C", ["outcome"] = "architecture-rule" }),
        ["existingChanged"] = new JsonObject { ["kept"] = 3, ["changed"] = 0, ["uncheckable"] = 0 },
        ["coverage"] = "not-measured",
    };

    private static JsonObject Verdict(JsonObject? structural, string redLevel) => new()
    {
        ["schema"] = CiEvidence.Schema,
        ["event"] = "pr",
        ["head"] = new string('a', 40),
        ["verdict"] = "PASS",
        ["structuralChange"] = structural,
        ["checks"] = new JsonArray(new JsonObject
        {
            ["id"] = "red",
            ["context"] = "lockflight/judge",
            ["verdict"] = redLevel == "off" ? "N/A" : "NOT-EARNED",
            ["requirement"] = redLevel,
            ["why"] = "x",
        }),
        ["contexts"] = new JsonObject { ["gates"] = "PASS", ["lockflight/judge"] = "PASS" },
    };
}
