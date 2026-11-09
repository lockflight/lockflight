using System.Text.Json;
using System.Text.Json.Nodes;
using Lockflight.Cli.Adoption;
using Lockflight.Cli.Ci;
using Lockflight.Cli.Config;
using Lockflight.Cli.EngineLocks;
using Lockflight.Cli.Evidence.Blocked;
using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Lifecycle.Approvals;
using Lockflight.Cli.Tests.Ci;

namespace Lockflight.Cli.Tests.Schemas;

/// <summary>
/// The current engine reads what each released engine wrote (<see cref="ReleasedFixtures"/>) and takes it to mean what that
/// release meant: the public contracts change only by addition (docs/guides/contracts.md).
/// </summary>
public sealed class ReleasedContractTests : IDisposable
{
    private const string FeatureId = "001-shipping";

    private readonly DirectoryInfo temp = Directory.CreateTempSubdirectory("lockflight-released-");

    [Fact]
    public void FixturesOfEveryPublishedMinorAreKept() => Assert.Contains("0.3.0", ReleasedFixtures.Versions);

    [Theory]
    [MemberData(nameof(ReleasedFixtures.All), MemberType = typeof(ReleasedFixtures))]
    public void TheManifestInitWroteStillLoadsAndValidates(string version)
    {
        string yaml = ReleasedFixtures.Read(version, "repository/lockflight.yaml");

        ManifestLoadResult result = new ManifestParser("/repo", "/repo/lockflight.yaml").Parse(yaml);

        Assert.True(result.Status == ManifestLoadStatus.Loaded, $"{version}: {result.Error?.Message}");
        ContractSchemaSyncTests.AssertValid("lockflight.manifest.v2", JsonSerializer.Serialize(YamlJson.Convert(yaml)));
    }

    [Theory]
    [MemberData(nameof(ReleasedFixtures.All), MemberType = typeof(ReleasedFixtures))]
    public void TheEngineLockStillPinsTheReleasedBinaries(string version)
    {
        string lockText = ReleasedFixtures.Read(version, "repository/.lockflight/engine.lock");
        string hashesText = ReleasedFixtures.Read(version, "engine-hashes.json");

        EngineLockReading reading = EngineLockReader.Read(lockText, allowDevelopment: false, "0.0.0");
        EngineHashes? hashes = EngineHashesReader.Read(hashesText, out string? problem);

        Assert.True(reading.Lock is not null, $"{version} engine.lock: {reading.Problem}");
        Assert.True(hashes is not null, $"{version} engine-hashes.json: {problem}");
        Assert.Equal(version, reading.Lock.Version);
        Assert.Equal(EngineLockChannel.Release, reading.Lock.Channel);
        Assert.Equal(version, hashes.Version);
        Assert.Equal(hashes.InstallScriptSha256, reading.Lock.InstallScriptSha256);
        Assert.Equal(hashes.Binaries.Keys.Order(StringComparer.Ordinal), reading.Lock.Binaries.Keys.Order(StringComparer.Ordinal));
        Assert.All(hashes.Binaries, binary => Assert.Equal(binary.Value.Sha256, reading.Lock.HashFor(binary.Key)));
        ContractSchemaSyncTests.AssertValid("lockflight.engine-lock.v1", lockText);
        ContractSchemaSyncTests.AssertValid("lockflight.engine-hashes.v1", hashesText);
    }

    [Theory]
    [MemberData(nameof(ReleasedFixtures.All), MemberType = typeof(ReleasedFixtures))]
    public void TheApprovalStillApprovesTheSpecItWasRecordedFor(string version)
    {
        string record = ReleasedFixtures.Read(version, "approval/spec.json");
        Feature feature = new(FeatureId, Path.Combine(temp.FullName, "specs", FeatureId), Path.Combine(temp.FullName, ".lockflight", "features", FeatureId));
        ApprovalStore store = new(feature);
        Directory.CreateDirectory(feature.SpecDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(store.RecordPath(ApprovalPhase.Spec))!);
        File.Copy(ReleasedFixtures.PathOf(version, "approval/spec.md"), Path.Combine(feature.SpecDirectory, "spec.md"));
        File.WriteAllText(store.RecordPath(ApprovalPhase.Spec), record);

        Assert.Equal(ApprovalState.Valid, store.Check(ApprovalPhase.Spec).State);
        ContractSchemaSyncTests.AssertValid("lockflight.approval.v2", record);
    }

    [Theory]
    [MemberData(nameof(ReleasedFixtures.All), MemberType = typeof(ReleasedFixtures))]
    public void TheJudgesEvidenceStillGivesTheVerdictOfRecordItGaveThen(string version)
    {
        JsonObject evidence = Json(version, "ci/evidence.json");
        string head = evidence["head"]!.GetValue<string>();

        Assert.Null(EvidenceShape.Problem(evidence));
        ContractSchemaSyncTests.AssertValid("lockflight.ci-evidence.v1", evidence.ToJsonString());
        ContractSchemaSyncTests.AssertValid("lockflight.result.v1", evidence["gates"]!.ToJsonString());
        JsonObject verdict = CiReportCommand.Join(evidence.DeepClone().AsObject(), head, review: null);
        foreach (string file in new[] { "ci/verdict-gates.json", "ci/verdict-judge.json" })
        {
            JsonObject released = Json(version, file);
            ContractSchemaSyncTests.AssertValid("lockflight.ci-evidence.v1", released.ToJsonString());
            Assert.Equal(released["verdict"]!.ToJsonString(), verdict["verdict"]!.ToJsonString());
            Assert.Equal(Contexts(released), Contexts(verdict));
            foreach (JsonNode? check in released["checks"]!.AsArray())
            {
                JsonNode now = verdict["checks"]!.AsArray().Single(candidate => candidate!["id"]!.ToJsonString() == check!["id"]!.ToJsonString())!;
                bool named = check!["reason"] is not null;
                Assert.Equal(Meaning(check, named), Meaning(now, named));
            }
        }
    }

    [Theory]
    [MemberData(nameof(ReleasedFixtures.All), MemberType = typeof(ReleasedFixtures))]
    public void TheStatusContextsAndTheCommentMarkerAreTheReleasedOnes(string version)
    {
        JsonObject released = Json(version, "ci/verdict-judge.json");
        string head = released["head"]!.GetValue<string>();
        string markerLine = $"{CommentRenderer.Marker} head={head} -->";

        Assert.All(released["contexts"]!.AsObject(), context => Assert.NotNull(StatusContextNames.Parse(context.Key)));
        foreach (string comment in new[] { "ci/comment-gates.md", "ci/comment-judge.md" })
        {
            Assert.Equal(markerLine, ReleasedFixtures.Read(version, comment).Split('\n')[0]);
        }

        Assert.Equal(markerLine, CommentRenderer.Render(released, CommentRenderer.MaxCommentLength).Split('\n')[0]);
    }

    [Theory]
    [MemberData(nameof(ReleasedFixtures.All), MemberType = typeof(ReleasedFixtures))]
    public async Task TheRedCommitsTrailerStillMarksACycle(string version)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using GitHistoryFixture git = await GitHistoryFixture.CreateAsync(cancellationToken);
        string before = await git.Write("a.txt", "a").CommitAsync("base", cancellationToken);
        string messageFile = Path.Combine(temp.FullName, "message.txt");
        File.Copy(ReleasedFixtures.PathOf(version, "red-commit.txt"), messageFile);
        await git.Write("a.txt", "b").GitAsync(["add", "-A"], cancellationToken);
        await git.GitAsync(["commit", "-q", "-F", messageFile], cancellationToken);
        string red = await git.HeadAsync(cancellationToken);

        IReadOnlyList<CommitTrailers> history = (await JudgeGit.HistoryAsync(git.Root, before, red, cancellationToken))!;
        RedCommitSelection selection = RedCommitSelector.Select(history, maxRedCommits: 10);

        RedCommit cycle = Assert.Single(selection.Cycles);
        Assert.Equal("1", cycle.Label);
        Assert.Equal(red, cycle.Commit);
    }

    [Theory]
    [MemberData(nameof(ReleasedFixtures.All), MemberType = typeof(ReleasedFixtures))]
    public void TheClaudeMdBlockInitWroteIsStillFound(string version)
    {
        string claudeMd = ReleasedFixtures.Read(version, "repository/CLAUDE.md");

        Assert.True(ClaudeMdBlock.IsPresent(claudeMd));
        Assert.Contains(ClaudeMdBlock.End, claudeMd, StringComparison.Ordinal);
        string upserted = ClaudeMdBlock.Upsert(claudeMd);
        Assert.Equal(1, Occurrences(upserted, ClaudeMdBlock.Begin));
        Assert.Equal(1, Occurrences(upserted, ClaudeMdBlock.End));
    }

    [Theory]
    [MemberData(nameof(ReleasedFixtures.All), MemberType = typeof(ReleasedFixtures))]
    public void TheReviewResultsAndBlockedReportsOfTheReleaseStillRead(string version)
    {
        foreach (string path in ReleasedFixtures.Files(version, "ci-review"))
        {
            string text = File.ReadAllText(path);
            Assert.True(JsonNode.Parse(text) is JsonObject review && CiReviewResult.Problem(review) is null, $"{version} {Path.GetFileName(path)}");
            ContractSchemaSyncTests.AssertValid("lockflight.ci-review.v1", text);
        }

        foreach (string path in ReleasedFixtures.Files(version, "blocked"))
        {
            string text = File.ReadAllText(path);
            Assert.True(BlockedReportReader.Read(text).Valid, $"{version} {Path.GetFileName(path)}");
            ContractSchemaSyncTests.AssertValid("lockflight.blocked.v1", text);
        }
    }

    public void Dispose() => TempDirectories.DeleteRecursively(temp);

    private static JsonObject Json(string version, string relativePath) => JsonNode.Parse(ReleasedFixtures.Read(version, relativePath))!.AsObject();

    private static string[] Contexts(JsonObject verdict) =>
        [.. verdict["contexts"]!.AsObject().Select(context => $"{context.Key} {context.Value}").Order(StringComparer.Ordinal)];

    /// <summary>
    /// What a check decides: its verdict, its level and, where the release named one, its reason code (a reason given where the
    /// release gave none is an addition).
    /// </summary>
    private static string Meaning(JsonNode check, bool withReason) =>
        $"{check["id"]} {check["verdict"]} {check["requirement"]}{(withReason ? $" {check["reason"]}" : string.Empty)}";

    private static int Occurrences(string text, string value) => (text.Length - text.Replace(value, string.Empty, StringComparison.Ordinal).Length) / value.Length;
}
