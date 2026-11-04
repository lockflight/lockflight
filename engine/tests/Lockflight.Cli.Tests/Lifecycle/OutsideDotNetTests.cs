using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Lifecycle;

public sealed class OutsideDotNetTests : IDisposable
{
    private const string CardSpec = "web/src/app/streak-card.spec.ts";

    private static readonly Requirement[] Requirements = [new("FR-001", 1, "first"), new("FR-006", 2, "card") { VerifiedOutside = [CardSpec] }];

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-outside-");

    [Fact]
    public void AFileThatNamesTheRequirementVerifiesIt()
    {
        OutsideFile file = OutsideVerification.Judge("FR-006", CardSpec, "it('shows the streak (FR-006)', () => {});");

        Assert.Equal(OutsideFileState.Names, file.State);
    }

    [Fact]
    public void AFileThatDoesNotNameTheRequirementDoesNotVerifyIt()
    {
        Assert.Equal(OutsideFileState.DoesNotName, OutsideVerification.Judge("FR-006", CardSpec, "it('shows the streak FR-0061', () => {});").State);
        Assert.Equal(OutsideFileState.Missing, OutsideVerification.Judge("FR-006", CardSpec, null).State);
    }

    [Theory]
    [InlineData("../other/web/card.spec.ts")]
    [InlineData("web/../../card.spec.ts")]
    [InlineData("/etc/card.spec.ts")]
    [InlineData("C:/web/card.spec.ts")]
    [InlineData("web\\..\\..\\card.spec.ts")]
    [InlineData("web/.. /card.spec.ts")]
    [InlineData("./web/card.spec.ts")]
    [InlineData("")]
    public void APathOutsideTheProjectNeverVerifies(string path)
    {
        Assert.False(OutsideVerification.IsInsideProject(path));
        Assert.Equal(OutsideFileState.OutsideProject, OutsideVerification.Judge("FR-006", path, "FR-006").State);
    }

    [Theory]
    [InlineData("specs/001-streak/spec.md")]
    [InlineData("Specs\\001-streak\\tasks.md")]
    public void TheSpecItselfNeverVerifiesARequirement(string path) =>
        Assert.Equal(OutsideFileState.InSpecs, OutsideVerification.Judge("FR-006", path, "- **FR-006** [outside .NET: x]: …").State);

    [Fact]
    public void ASymbolicLinkIsNotFollowedLocally()
    {
        string outsideFile = Path.Combine(root.FullName, "elsewhere.txt");
        File.WriteAllText(outsideFile, "FR-006");
        Directory.CreateDirectory(Path.Combine(root.FullName, "web"));
        File.CreateSymbolicLink(Path.Combine(root.FullName, "web", "card.spec.ts"), outsideFile);
        Requirement requirement = new("FR-006", 1, "card") { VerifiedOutside = ["web/card.spec.ts"] };

        Assert.Equal(OutsideFileState.Missing, Assert.Single(OutsideVerification.ReadLocal(requirement, root.FullName)).State);
    }

    [Fact]
    public void AMarkerDoesNotHideADotNetTestThatWasLost()
    {
        RedGreenRecord record = Record();
        record.Cycles[0] = record.Cycles[0] with { Tests = [Green("First", "FR-001"), Green("Card", "FR-006")] };

        IReadOnlyList<TraceRow> rows = TraceMatrix.Build(Requirements, record, [Test("First", "FR-001")], Verified("FR-006"));

        Assert.False(rows[1].Excluded);
        Assert.Equal(["FR-006"], TraceTally.Of(rows).Uncovered);
        Assert.Contains("no longer counts", rows[1].OutsideNote, StringComparison.Ordinal);
    }

    [Fact]
    public void ARequirementDefinedTwiceWithAMarkerOnOneLineIsReported()
    {
        SpecDocument spec = SpecDocument.Parse("- **FR-001**: First\n- **FR-001**: Again\n- **FR-006**: API\n- **FR-006** [outside .NET: web/a.spec.ts]: Card\n");

        Assert.Equal(["FR-006"], spec.DuplicateMarkedIds);
    }

    [Fact]
    public void LocalFilesAreReadFromTheProjectRoot()
    {
        Directory.CreateDirectory(Path.Combine(root.FullName, "web", "src", "app"));
        File.WriteAllText(Path.Combine(root.FullName, CardSpec), "describe('FR-006 streak card', () => {});");
        Requirement requirement = new("FR-006", 1, "The streak card MUST show the streak") { VerifiedOutside = [CardSpec, "web/src/app/missing.spec.ts"] };

        IReadOnlyList<OutsideFile> files = OutsideVerification.ReadLocal(requirement, root.FullName);

        Assert.Equal([OutsideFileState.Names, OutsideFileState.Missing], files.Select(file => file.State));
    }

    [Fact]
    public void AVerifiedRequirementIsExcludedNeitherCoveredNorFailing()
    {
        IReadOnlyList<TraceRow> rows = TraceMatrix.Build(Requirements, Record(), [Test("First", "FR-001")], Verified("FR-006"));

        Assert.True(rows[1].Excluded);
        Assert.False(rows[1].Covered);
        Assert.Equal($"verified outside .NET ({CardSpec})", rows[1].OutsideNote);
        TraceTally tally = TraceTally.Of(rows);
        Assert.Equal(TraceOutcome.Pass, tally.Outcome);
        Assert.Equal((1, 1), (tally.Covered, tally.Excluded));
    }

    [Fact]
    public void AMissingOrSilentFileLeavesTheRequirementUncovered()
    {
        Dictionary<string, IReadOnlyList<OutsideFile>> outside = new(StringComparer.Ordinal)
        {
            ["FR-006"] = [new OutsideFile(CardSpec, OutsideFileState.DoesNotName)],
        };

        IReadOnlyList<TraceRow> rows = TraceMatrix.Build(Requirements, Record(), [Test("First", "FR-001")], outside);

        Assert.False(rows[1].Excluded);
        Assert.Equal($"{CardSpec} does not name FR-006", rows[1].OutsideNote);
        TraceTally tally = TraceTally.Of(rows);
        Assert.Equal(TraceOutcome.Fail, tally.Outcome);
        Assert.Equal(["FR-006"], tally.Uncovered);
    }

    [Fact]
    public void ADotNetTestNamingAnExcludedRequirementCoversIt()
    {
        RedGreenRecord record = Record();
        record.Cycles[0] = record.Cycles[0] with { Tests = [Green("First", "FR-001"), Green("Card", "FR-006")] };

        IReadOnlyList<TraceRow> rows = TraceMatrix.Build(Requirements, record, [Test("First", "FR-001"), Test("Card", "FR-006")], Verified("FR-006"));

        Assert.True(rows[1].Covered);
        Assert.False(rows[1].Excluded);
        Assert.Equal((2, 0), (TraceTally.Of(rows).Covered, TraceTally.Of(rows).Excluded));
    }

    [Fact]
    public void EveryRequirementExcludedIsNotApplicableNeverPass()
    {
        Dictionary<string, IReadOnlyList<OutsideFile>> outside = new(StringComparer.Ordinal)
        {
            ["FR-001"] = [new OutsideFile(CardSpec, OutsideFileState.Names)],
            ["FR-006"] = [new OutsideFile(CardSpec, OutsideFileState.Names)],
        };

        RedGreenRecord record = Record();
        record.Cycles.Clear();

        IReadOnlyList<TraceRow> rows = TraceMatrix.Build(Requirements, record, [], outside);

        Assert.Equal(TraceOutcome.NotApplicable, TraceTally.Of(rows).Outcome);
    }

    [Fact]
    public void AMarkerAddedOrChangedOnARequirementOfTheBaseIsReported()
    {
        IReadOnlyList<Requirement> before = SpecDocument.Parse("- **FR-001**: First\n- **FR-002**: Second\n- **FR-003** [outside .NET: a.spec.ts]: Third\n").Requirements;
        IReadOnlyList<Requirement> after = SpecDocument.Parse("- **FR-001** [outside .NET: b.spec.ts]: First\n- **FR-002**: Second\n- **FR-003** [outside .NET: c.spec.ts]: Third\n- **FR-004** [outside .NET: d.spec.ts]: New\n").Requirements;

        IReadOnlyList<string> changes = RequirementMarkers.ChangesSince(before, after);

        Assert.Equal(["FR-001: [outside .NET: b.spec.ts] added", "FR-003: [outside .NET: a.spec.ts] changed to [outside .NET: c.spec.ts]"], changes);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private static Dictionary<string, IReadOnlyList<OutsideFile>> Verified(string requirement) =>
        new(StringComparer.Ordinal) { [requirement] = [new OutsideFile(CardSpec, OutsideFileState.Names)] };

    private static RedGreenRecord Record()
    {
        RedGreenRecord record = new()
        {
            BaselineCommit = "abc",
            BaselineAt = DateTimeOffset.UnixEpoch,
            BaselineTests = new HashSet<string>(StringComparer.Ordinal),
            TestProjects = ["tests"],
        };
        record.Cycles.Add(new RedGreenCycle(1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, [Green("First", "FR-001")]));
        return record;
    }

    private static RedGreenTest Green(string method, string requirement) =>
        new($"Shop.Tests:Shop.Tests.StreakTests.{method}", $"Shop.Tests.StreakTests.{method}", RedClass.RedStub, [requirement]);

    private static TestCase Test(string method, params string[] requirements) => new("Shop.Tests", "Shop.Tests.StreakTests", method, requirements, null);
}
