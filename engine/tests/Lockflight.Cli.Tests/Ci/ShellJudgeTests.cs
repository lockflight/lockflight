using System.Text.Json.Nodes;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Ci;

/// <summary><c>lockflight ci --shell</c> on a real build of a unit repository whose spec lives in its workspace shell.</summary>
[Trait("Category", "CiIntegration")]
public sealed class ShellJudgeTests : IAsyncLifetime
{
    private const string ShellUrl = "https://example.com/shell.git";
    private const string Stub = "    public decimal Shipping(decimal kilograms) => throw new NotImplementedException();";
    private const string Implementation = "    public decimal Shipping(decimal kilograms) => kilograms * 2m;";
    private const string Test = "    [Fact]\n    [Trait(\"Requirement\", \"FR-001\")]\n    public void ShippingIsTwiceTheWeight() => Assert.Equal(4m, new Price().Shipping(2m));";

    private readonly DirectoryInfo evidence = Directory.CreateTempSubdirectory("lockflight-ci-shell-");
    private GitHistoryFixture unit = null!;
    private GitHistoryFixture shell = null!;
    private string main = string.Empty;
    private string head = string.Empty;

    public async ValueTask InitializeAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        shell = await GitHistoryFixture.CreateAsync(cancellationToken);
        await shell.GitAsync(["remote", "add", "origin", ShellUrl], cancellationToken);
        await shell.Write("lockflight.yaml", "version: 2\nunits:\n  - path: services/shop\n").Write("CODEOWNERS", "/specs/ @org/product\n").CommitAsync("shell", cancellationToken);
        unit = await GitHistoryFixture.CreateAsync(cancellationToken);
        main = await unit.WriteSolution(string.Empty, string.Empty)
            .Write("lockflight.yaml", $"version: 2\nshell:\n  url: {ShellUrl}\n  unit: services/shop\n")
            .CommitAsync("init", cancellationToken);
        await unit.CheckoutNewAsync("001-shipping", cancellationToken);
        await unit.WriteCode(Stub, Test).CommitAsync("test(001-shipping): red for US1", cancellationToken, null, "Lockflight-Red: 1");
        head = await unit.WriteCode(Implementation, Test).CommitAsync("feat(001-shipping): US1", cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        unit.Dispose();
        shell.Dispose();
        TempDirectories.DeleteRecursively(evidence);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task AUnitPullRequestIsNotEarnedUntilTheShellsSpecMergesThenTracedAgainstIt()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        JsonNode waiting = await JudgeAsync();
        await shell.Write("specs/001-shipping/spec.md", "# Shipping\n\n## Requirements\n\n- **FR-001**: Shipping MUST cost twice the weight\n- **FR-002**: Another unit's part\n")
            .CommitAsync("spec(001-shipping): approved", cancellationToken);
        JsonNode merged = await JudgeAsync();

        Assert.Equal("NOT-EARNED", Check(waiting, "trace")["verdict"]!.GetValue<string>());
        Assert.Equal("spec-not-on-base", waiting["blocked"]!["reason"]!["code"]!.GetValue<string>());
        Assert.Equal("PASS", Check(waiting, "gates")["verdict"]!.GetValue<string>());
        Assert.Equal("PASS", Check(merged, "trace")["verdict"]!.GetValue<string>());
        Assert.StartsWith("1/1 requirements", Check(merged, "trace")["why"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("shell", merged["feature"]!["spec"]!["origin"]!.GetValue<string>());
        Assert.Equal(ShellUrl, merged["feature"]!["spec"]!["shell"]!["url"]!.GetValue<string>());
        Assert.Null(merged["blocked"]);
    }

    private async Task<JsonNode> JudgeAsync()
    {
        string path = Path.Combine(evidence.FullName, "evidence.json");
        File.Delete(path);
        (ExitCode exitCode, string output, string error) = await TestConsole.RunAsync(
            ["ci", "--platform", "github", "--head", head, "--base", main, "--branch", "001-shipping", "--evidence", evidence.FullName, "--shell", shell.Root], string.Empty, unit.Root,
            new Dictionary<string, string> { [EnvironmentVariables.Temp] = evidence.FullName, ["PATH"] = Environment.GetEnvironmentVariable("PATH") ?? string.Empty, ["HOME"] = Environment.GetEnvironmentVariable("HOME") ?? string.Empty });
        Assert.True(File.Exists(path), $"exit {(int)exitCode}: {output}\n{error}");
        return JsonNode.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken))!;
    }

    private static JsonNode Check(JsonNode document, string id) => document["checks"]!.AsArray().Single(check => check!["id"]!.GetValue<string>() == id)!;
}
