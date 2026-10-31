using System.Text.Json.Nodes;
using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Lifecycle.Approvals;

namespace Lockflight.Cli.Tests.Lifecycle.Approvals;

public sealed class ApprovalRecordVersionTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-approval-v2-");

    public ApprovalRecordVersionTests()
    {
        Directory.CreateDirectory(Feature.SpecDirectory);
        File.WriteAllText(Feature.SpecPath, "# Spec\n");
    }

    private Feature Feature => new("f1", Path.Combine(root.FullName, "specs", "f1"), Path.Combine(root.FullName, ".lockflight", "features", "f1"));

    [Fact]
    public void AnApprovalIsWrittenAsVersionTwoByAPerson()
    {
        ApprovalStore store = new(Feature);
        string path = store.Save(ApprovalPhase.Spec, "Jane Doe", new DateTimeOffset(2026, 11, 2, 11, 15, 0, TimeSpan.FromHours(1)), "0.3.0");

        JsonObject record = JsonNode.Parse(File.ReadAllText(path))!.AsObject();

        Assert.Equal(["schema", "feature", "phase", "artifacts", "state", "approvedBy", "at", "engineVersion", "rule"], record.Select(property => property.Key));
        Assert.Equal("lockflight.approval/v2", (string?)record["schema"]);
        Assert.Equal("person", (string?)record["state"]);
        Assert.Equal("2026-11-02T10:15:00Z", (string?)record["at"]);
        Assert.Null(record["rule"]);
        Assert.Equal(ApprovalState.Valid, store.Check(ApprovalPhase.Spec).State);
    }

    [Fact]
    public void AVersionOneRecordStillApprovesAsAPersons()
    {
        ApprovalStore store = new(Feature);
        string path = store.Save(ApprovalPhase.Spec, "Jane Doe", DateTimeOffset.UnixEpoch, "0.2.0");
        Rewrite(path, record =>
        {
            record["schema"] = "lockflight.approval/v1";
            record.Remove("state");
            record.Remove("rule");
            record["at"] = "1970-01-01T00:00:00.0000000+00:00";
        });

        ApprovalCheck check = store.Check(ApprovalPhase.Spec);

        Assert.Equal((ApprovalState.Valid, "Jane Doe"), (check.State, check.ApprovedBy));
    }

    [Theory]
    [InlineData("skipped-by-policy", "it records a step skipped by policy, which approves nothing in this engine version")]
    [InlineData("platform", "its state 'platform' is not one this engine knows")]
    [InlineData(null, "it has no 'state'")]
    public void ARecordThatIsNotAPersonsApprovesNothing(string? state, string problem)
    {
        ApprovalStore store = new(Feature);
        string path = store.Save(ApprovalPhase.Spec, "Jane Doe", DateTimeOffset.UnixEpoch, "0.3.0");
        Rewrite(path, record => record["state"] = state);

        ApprovalCheck check = store.Check(ApprovalPhase.Spec);

        Assert.Equal(ApprovalState.Invalid, check.State);
        Assert.Contains(problem, check.Describe(root.FullName), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownSchemaApprovesNothing()
    {
        ApprovalStore store = new(Feature);
        string path = store.Save(ApprovalPhase.Spec, "Jane Doe", DateTimeOffset.UnixEpoch, "0.3.0");
        Rewrite(path, record => record["schema"] = "lockflight.approval/v3");

        Assert.Contains("its schema is 'lockflight.approval/v3'", store.Check(ApprovalPhase.Spec).Describe(root.FullName), StringComparison.Ordinal);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private static void Rewrite(string path, Action<JsonObject> change)
    {
        JsonObject record = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        change(record);
        File.WriteAllText(path, record.ToJsonString());
    }
}
