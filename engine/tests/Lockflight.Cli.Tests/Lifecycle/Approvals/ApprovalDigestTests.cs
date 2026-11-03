using System.Text;
using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Lifecycle.Approvals;

namespace Lockflight.Cli.Tests.Lifecycle.Approvals;

public sealed class ApprovalDigestTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-digest-");

    private Feature Feature => new("f1", Path.Combine(root.FullName, "specs", "f1"), Path.Combine(root.FullName, ".lockflight", "features", "f1"));

    [Fact]
    public void TheDigestDescribesTheContentWhoseHashItShowsNotWhatIsOnDiskLater()
    {
        Directory.CreateDirectory(Feature.SpecDirectory);
        File.WriteAllText(Feature.SpecPath, "- **FR-001**: Shown\n");
        ApprovalStore store = new(Feature);
        IReadOnlyList<ArtifactSnapshot> snapshot = store.Snapshot(ApprovalPhase.Spec);
        File.WriteAllText(Feature.SpecPath, "- **FR-009**: Swapped in [NEEDS CLARIFICATION: x]\n");

        string digest = ApprovalDigest.Render(Feature, ApprovalPhase.Spec, snapshot, store.Check(ApprovalPhase.Spec), "k7m2qx");

        Assert.Contains("Requirements (1): FR-001", digest, StringComparison.Ordinal);
        Assert.DoesNotContain("FR-009", digest, StringComparison.Ordinal);
        Assert.Contains("markers: none", digest, StringComparison.Ordinal);
        Assert.Contains(snapshot[0].Hash[..12], digest, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDigestListsEachRequirementVerifiedOutsideDotNetWithItsTextAndFiles()
    {
        Directory.CreateDirectory(Feature.SpecDirectory);
        File.WriteAllText(Feature.SpecPath, "- **FR-001**: The API MUST return the streak\n- **FR-006** [outside .NET: web/streak-card.spec.ts]: The card MUST show the streak\n");
        ApprovalStore store = new(Feature);

        string digest = ApprovalDigest.Render(Feature, ApprovalPhase.Spec, store.Snapshot(ApprovalPhase.Spec), store.Check(ApprovalPhase.Spec), "k7m2qx");

        Assert.Contains("Verified outside .NET, not traced to .NET tests (1):", digest, StringComparison.Ordinal);
        Assert.Contains("FR-006 The card MUST show the streak — web/streak-card.spec.ts", digest, StringComparison.Ordinal);
    }

    [Fact]
    public void ASnapshotHashesItsBytesLikeTheFileHash()
    {
        Directory.CreateDirectory(Feature.SpecDirectory);
        File.WriteAllBytes(Feature.SpecPath, [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("# Spec\r\n")]);

        ArtifactSnapshot spec = new ApprovalStore(Feature).Snapshot(ApprovalPhase.Spec)[0];

        Assert.Equal(ArtifactHash.Of(Feature.SpecPath), spec.Hash);
        Assert.Equal("# Spec\r\n", spec.Text);
    }

    [Fact]
    public void AMissingArtifactIsAbsent()
    {
        ArtifactSnapshot plan = new ApprovalStore(Feature).Snapshot(ApprovalPhase.Tasks)[1];

        Assert.Equal("plan.md", plan.Name);
        Assert.Equal("absent", plan.Hash);
        Assert.Null(plan.Text);
    }

    public void Dispose() => root.Delete(recursive: true);
}
