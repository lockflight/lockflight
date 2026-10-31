using System.Text.Json.Nodes;
using Lockflight.Cli.Config;
using Lockflight.Cli.Init;

namespace Lockflight.Cli.Tests.Init;

public sealed class SandboxSettingsTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-sandbox-");

    [Fact]
    public void OnlyProtectedPathsThatExistAreDenied()
    {
        Write(Manifest.FileName, "version: 1\n");
        Write("dotnet-tools.json", "{}");

        IReadOnlyList<string> denied = SandboxSettings.DenyWrite(root.FullName, DefaultProtectedPaths.Globs);

        Assert.Contains("./lockflight.yaml", denied);
        Assert.Contains("./dotnet-tools.json", denied);
        Assert.DoesNotContain("./global.json", denied);
        Assert.DoesNotContain("./.claude/hooks", denied);
    }

    [Fact]
    public void APatternIsDeniedAsEachFileItMatchesOutsideBuildOutput()
    {
        Write("Directory.Build.props", "<Project />");
        Write("src/App/Directory.Build.props", "<Project />");
        Write("src/App/obj/Directory.Build.props", "<Project />");
        Write(".claude/worktrees/agent-1/Directory.Build.props", "<Project />");

        IReadOnlyList<string> denied = SandboxSettings.DenyWrite(root.FullName, DefaultProtectedPaths.Globs);

        Assert.Contains("./Directory.Build.props", denied);
        Assert.Contains("./src/App/Directory.Build.props", denied);
        Assert.DoesNotContain(denied, path => path.Contains("obj/", StringComparison.Ordinal) || path.Contains("worktrees", StringComparison.Ordinal));
        Assert.DoesNotContain(denied, path => path.Contains('*', StringComparison.Ordinal));
    }

    [Fact]
    public void AProtectedDirectoryIsDeniedAsAWhole()
    {
        Write(".claude/hooks/guard.sh", "exit 0");

        IReadOnlyList<string> denied = SandboxSettings.DenyWrite(root.FullName, DefaultProtectedPaths.Globs);

        Assert.Contains("./.claude/hooks", denied);
    }

    [Fact]
    public void OfTheLockflightDirectoryOnlyTheEngineTheBaselinesTheLockAndTheInstallScriptAreDenied()
    {
        Write(".lockflight/baselines/known-failing-tests.json", "[]");
        Write(".lockflight/ledger/2026-10.jsonl", string.Empty);
        Write(".lockflight/engine.lock", "{}");
        Write(".lockflight/ci/install.sh", "exit 0");

        IReadOnlyList<string> denied = SandboxSettings.DenyWrite(root.FullName, DefaultProtectedPaths.Globs);

        Assert.Contains("./.lockflight/bin", denied);
        Assert.Contains("./.lockflight/baselines", denied);
        Assert.Contains("./.lockflight/engine.lock", denied);
        Assert.Contains("./.lockflight/ci", denied);
        Assert.DoesNotContain("./.lockflight", denied);
        Assert.DoesNotContain(denied, path => path.StartsWith("./.lockflight/ledger", StringComparison.Ordinal));
    }

    [Fact]
    public void MergeAddsWhatIsMissingAndKeepsTheTeamsEntries()
    {
        JsonObject settings = JsonNode.Parse("""{ "sandbox": { "enabled": false, "filesystem": { "denyWrite": ["/etc", "./lockflight.yaml"] } } }""")!.AsObject();

        bool changed = SandboxSettings.Merge(settings, ["./lockflight.yaml", "./.lockflight/bin"]);

        Assert.True(changed);
        Assert.Equal(["/etc", "./lockflight.yaml", "./.lockflight/bin"], SandboxSettings.Entries(settings, SandboxSettings.DenyWriteKey));
        Assert.Equal(SandboxSettings.DotnetCaches, SandboxSettings.Entries(settings, SandboxSettings.AllowWriteKey));
        Assert.False(settings["sandbox"]!["enabled"]!.GetValue<bool>());
        Assert.False(SandboxSettings.Merge(settings, ["./lockflight.yaml"]));
    }

    [Theory]
    [InlineData("""{ "sandbox": true }""")]
    [InlineData("""{ "sandbox": { "filesystem": [] } }""")]
    [InlineData("""{ "sandbox": { "filesystem": { "denyWrite": "./lockflight.yaml" } } }""")]
    [InlineData("""{ "sandbox": { "filesystem": { "allowWrite": [1] } } }""")]
    public void SandboxSettingsOfAnotherShapeAreAProblem(string json) =>
        Assert.NotNull(SandboxSettings.Problem(JsonNode.Parse(json)!.AsObject()));

    private void Write(string relative, string text)
    {
        string path = Path.Combine(root.FullName, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    public void Dispose() => root.Delete(recursive: true);
}
