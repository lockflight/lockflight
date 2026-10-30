using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Lockflight.Cli.Ci;
using Lockflight.Cli.Config;
using Lockflight.Cli.EngineLocks;
using Lockflight.Cli.Evidence.Blocked;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Lifecycle.Approvals;
using Lockflight.Cli.Tests.Ci;

namespace Lockflight.Cli.Tests.Schemas;

/// <summary>
/// Every contract frozen at 0.3.0 has a published JSON Schema under <c>schemas/</c>. Where the engine reads the contract, its reader
/// and the schema agree on every fixture; where the engine writes it, what it writes is valid.
/// </summary>
public sealed class ContractSchemaSyncTests : IDisposable
{
    private const string Blocked = "lockflight.blocked.v1";
    private const string EngineLockId = "lockflight.engine-lock.v1";
    private const string EngineHashesId = "lockflight.engine-hashes.v1";
    private const string Approval = "lockflight.approval.v2";
    private const string Result = "lockflight.result.v1";
    private const string CiEvidenceId = "lockflight.ci-evidence.v1";

    /// <summary>The frozen list (contracts-0.3.md §4.5), the referenced schemas before the one that references them; the trailer is plain text.</summary>
    private static readonly string[] Frozen = ["lockflight.manifest.v2", EngineLockId, EngineHashesId, Approval, Result, Blocked, CiEvidenceId];

    private static readonly Dictionary<string, JsonSchema> Schemas = Load();

    private readonly DirectoryInfo temp = Directory.CreateTempSubdirectory("lockflight-schemas-");

    /// <summary>The contracts the engine reads, whose fixtures both must agree on.</summary>
    private static readonly string[] Read = [Blocked, EngineLockId, EngineHashesId, Approval];

    private static readonly string[] Kinds = ["valid", "invalid"];

    public static TheoryData<string, string, bool> Fixtures() =>
    [
        .. Read.SelectMany(id => Kinds.SelectMany(kind => SchemaFiles.Fixtures(id, kind).Select(path => (id, Path.GetFileName(path), kind == "valid")))),
    ];

    [Fact]
    public void EveryFrozenContractHasAPublishedSchemaNamedAfterItsId()
    {
        foreach (string id in Frozen)
        {
            JsonNode schema = JsonNode.Parse(File.ReadAllText(Path.Combine(SchemaFiles.Directory, $"{id}.json")))!;
            Assert.Equal($"https://raw.githubusercontent.com/lockflight/lockflight/main/schemas/{id}.json", schema["$id"]!.GetValue<string>());
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void TheEnginesReaderAndTheSchemaAgreeOnEveryFixture(string id, string fixture, bool valid)
    {
        string kind = valid ? "valid" : "invalid";
        string text = File.ReadAllText(Path.Combine(SchemaFiles.Directory, "fixtures", id, kind, fixture));

        Assert.True(EngineAccepts(id, text) == valid, $"the engine {(valid ? "refuses" : "accepts")} {id}/{kind}/{fixture}");
        Assert.True(Accepts(id, text) == valid, $"the schema {(valid ? "refuses" : "accepts")} {id}/{kind}/{fixture}; update schemas/{id}.json");
    }

    [Fact]
    public async Task TheGateResultTheEngineWritesIsValid()
    {
        string path = Path.Combine(temp.FullName, "result.json");
        GateResult result = new("/repo", GateProfile.PullRequest, Verdict.Fail, 3,
        [
            new GateStep("build", 0, Verdict.Pass, 1200, string.Empty),
            new GateStep("services/orders: test", 1, Verdict.Fail, 3400, "1 failed") { Id = "test", Unit = "services/orders" },
        ]);

        await GateResultWriter.WriteAsync(path, result, GateSettings.Default, "0.3.0", new string('b', 40), null, DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken);

        AssertValid(Result, File.ReadAllText(path));
    }

    [Fact]
    public void TheApprovalRecordTheEngineWritesIsValid()
    {
        Feature feature = new("f1", Path.Combine(temp.FullName, "specs", "f1"), Path.Combine(temp.FullName, ".lockflight", "features", "f1"));

        string path = new ApprovalStore(feature).Save(ApprovalPhase.Spec, [("spec.md", new string('a', 64)), ("plan.md", ArtifactSnapshot.Absent)], "Jane Doe", DateTimeOffset.UnixEpoch, "0.3.0");

        AssertValid(Approval, File.ReadAllText(path));
    }

    [Fact]
    public void TheEngineLockTheEngineWritesIsValid() =>
        AssertValid(EngineLockId, EngineLockText.Write(new EngineLock("0.3.0", EngineLockChannel.Release, new string('a', 64),
            EngineRids.Released.ToDictionary(rid => rid, _ => new string('b', 64)))));

    [Fact]
    public void TheCiEvidenceAndTheVerdictOfRecordAreValid()
    {
        JsonObject evidence = CiEvidenceTests.Evidence();
        string head = evidence["head"]!.GetValue<string>();

        AssertValid(CiEvidenceId, evidence.ToJsonString());
        AssertValid(CiEvidenceId, CiReportCommand.Join(evidence.DeepClone().AsObject(), head, null).ToJsonString());
        AssertValid(CiEvidenceId, CiReportCommand.Join(null, head, null).ToJsonString());
        foreach (string reviewed in new[] { head, new string('e', 40) })
        {
            JsonObject review = new() { ["schema"] = "lockflight.ci-review/v1", ["head"] = reviewed, ["verdict"] = "PASS", ["why"] = "no findings" };
            AssertValid(CiEvidenceId, CiReportCommand.Join(evidence.DeepClone().AsObject(), head, review).ToJsonString());
        }

        Assert.False(Accepts(CiEvidenceId, Embedding(evidence, "gates", new JsonObject { ["schema"] = "lockflight.result/v1" })), "the embedded gate result is not checked");
        Assert.False(Accepts(CiEvidenceId, Embedding(evidence, "blocked", new JsonObject { ["schema"] = "lockflight.blocked/v1" })), "the embedded blocked report is not checked");
    }

    [Fact]
    public void TheBlockedReportsTheJudgeWritesAreValid()
    {
        BlockedReport review = JudgeBlocked.ForReview(DateTimeOffset.UnixEpoch, new string('a', 40), "001-shipping");

        AssertValid(Blocked, BlockedReportJson.Render(review).ToJsonString());
        Assert.True(BlockedReportReader.Read(BlockedReportJson.Render(review).ToJsonString()).Valid);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(temp);

    /// <summary>Asserts the document is valid against the contract's schema, naming the first errors.</summary>
    internal static void AssertValid(string id, string json)
    {
        EvaluationResults results = Schemas[id].Evaluate(JsonDocument.Parse(json).RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        string errors = string.Join("; ", (results.Details ?? []).Where(detail => detail.Errors is not null)
            .SelectMany(detail => detail.Errors!.Select(error => $"{detail.InstanceLocation}: {error.Value}")).Take(5));
        Assert.True(results.IsValid, $"{id}: {errors}");
    }

    private static string Embedding(JsonObject evidence, string property, JsonObject value)
    {
        JsonObject copy = evidence.DeepClone().AsObject();
        copy[property] = value;
        return copy.ToJsonString();
    }

    private static bool Accepts(string id, string json) => Schemas[id].Evaluate(JsonDocument.Parse(json).RootElement).IsValid;

    private bool EngineAccepts(string id, string text) => id switch
    {
        Blocked => BlockedReportReader.Read(text).Valid,
        EngineLockId => EngineLockReader.Read(text, allowDevelopment: true, "0.3.0").Lock is not null,
        EngineHashesId => EngineHashesReader.Read(text, out _) is not null,
        Approval => ApprovalReadsAsValid(text),
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "No engine reader for this contract."),
    };

    /// <summary>The record, saved as feature f1's spec approval next to a spec whose hash it names, approves it.</summary>
    private bool ApprovalReadsAsValid(string text)
    {
        Feature feature = new("f1", Path.Combine(temp.FullName, "specs", "f1"), Path.Combine(temp.FullName, ".lockflight", "features", "f1"));
        ApprovalStore store = new(feature);
        Directory.CreateDirectory(Path.GetDirectoryName(store.RecordPath(ApprovalPhase.Spec))!);
        File.WriteAllText(store.RecordPath(ApprovalPhase.Spec), text);
        return store.Check(ApprovalPhase.Spec, [("spec.md", new string('a', 64))]).State == ApprovalState.Valid;
    }

    private static Dictionary<string, JsonSchema> Load()
    {
        BuildOptions options = new() { SchemaRegistry = new SchemaRegistry() };
        return Frozen.ToDictionary(id => id, id => JsonSchema.FromFile(Path.Combine(SchemaFiles.Directory, $"{id}.json"), options), StringComparer.Ordinal);
    }
}
