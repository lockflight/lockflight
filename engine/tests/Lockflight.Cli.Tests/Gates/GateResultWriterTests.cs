using System.Text.Json.Nodes;
using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;

namespace Lockflight.Cli.Tests.Gates;

public sealed class GateResultWriterTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-result-");

    [Fact]
    public async Task TheResultNamesTheEngineTheCommitsAndEachStepsIdAndMode()
    {
        GateResult result = new("/repo", GateProfile.PullRequest, Verdict.Pass, 12,
        [
            new GateStep("build", 0, Verdict.Pass, 1200, string.Empty),
            new GateStep("services/orders: test", 0, Verdict.Pass, 3400, "12 passed") { Id = "test", Unit = "services/orders" },
            new GateStep("quality.suppressions", 0, Verdict.Pass, 5, "report only"),
        ]);
        GateSettings gates = GateSettings.Default with { Suppressions = GateMode.Report };
        string path = Path.Combine(root.FullName, "out", "result.json");

        await GateResultWriter.WriteAsync(
            path, result, gates, "0.3.0", new string('b', 40), new string('c', 40), new DateTimeOffset(2026, 11, 2, 10, 15, 0, TimeSpan.Zero), TestContext.Current.CancellationToken);

        JsonObject written = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal(["schema", "engineVersion", "at", "target", "profile", "base", "head", "verdict", "testsTotal", "steps"], written.Select(property => property.Key));
        Assert.Equal(("lockflight.result/v1", "0.3.0", "2026-11-02T10:15:00Z"), ((string?)written["schema"], (string?)written["engineVersion"], (string?)written["at"]));
        Assert.Equal((new string('b', 40), new string('c', 40)), ((string?)written["base"], (string?)written["head"]));
        JsonArray steps = written["steps"]!.AsArray();
        Assert.Equal(["build", "test", "quality.suppressions"], steps.Select(step => (string?)step!["id"]));
        Assert.Equal(["fail", "fail", "report"], steps.Select(step => (string?)step!["mode"]));
        Assert.Equal(["services/orders: test", "services/orders"], new[] { (string?)steps[1]!["name"], (string?)steps[1]!["unit"] });
        Assert.Null(steps[0]!["unit"]);
    }

    [Fact]
    public async Task WithoutABaseOrARepositoryTheCommitsAreNull()
    {
        string path = Path.Combine(root.FullName, "result.json");

        await GateResultWriter.WriteAsync(
            path, new GateResult("/repo", GateProfile.Stop, Verdict.Pass, null, []), GateSettings.Default, "0.3.0", null, null, DateTimeOffset.UnixEpoch,
            TestContext.Current.CancellationToken);

        JsonObject written = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.True(written.ContainsKey("base") && written["base"] is null && written["head"] is null);
    }

    [Theory]
    [InlineData("ef.pendingModelChanges", "auto")]
    [InlineData("docs.references", "report")]
    [InlineData("security.secrets", "fail")]
    public void EachStepTakesTheModeOfItsKey(string id, string mode) =>
        Assert.Equal(mode, GateModeNames.ToName(GateResultWriter.ModeOf(id, GateSettings.Default)));

    public void Dispose() => TempDirectories.DeleteRecursively(root);
}
