using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Facts;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Facts;

public sealed class FactsCommandTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-facts-");

    [Fact]
    public async Task TheJsonNamesItsSchemaAndTheEffectiveProtectedPaths()
    {
        Write(Manifest.FileName, "protected:\n  - secrets/**\n");
        await CommitAsync();

        using JsonDocument facts = await FactsAsync();

        Assert.Equal(FactsJson.Schema, facts.RootElement.GetProperty("schema").GetString());
        string[] protectedPaths = [.. facts.RootElement.GetProperty("protectedPaths").EnumerateArray().Select(path => path.GetString()!)];
        Assert.Contains("secrets/**", protectedPaths);
        Assert.Contains(Manifest.FileName, protectedPaths);
        Assert.Equal(Manifest.FileName, facts.RootElement.GetProperty("manifest").GetProperty("file").GetString());
    }

    [Fact]
    public async Task WithoutAManifestTheProtectedPathsAreNull()
    {
        Write("README.md", "# Shop\n");
        await CommitAsync();

        using JsonDocument facts = await FactsAsync();

        Assert.Equal(JsonValueKind.Null, facts.RootElement.GetProperty("manifest").ValueKind);
        Assert.Equal(JsonValueKind.Null, facts.RootElement.GetProperty("protectedPaths").ValueKind);
    }

    [Fact]
    public async Task EachProfileListsItsStepsWithTheirModes()
    {
        Write(Manifest.FileName, "gates:\n  quality:\n    suppressions: fail\n");
        await CommitAsync();

        using JsonDocument facts = await FactsAsync();

        JsonElement pr = facts.RootElement.GetProperty("gates").GetProperty("profiles").EnumerateArray()
            .Single(profile => profile.GetProperty("name").GetString() == "pr");
        Dictionary<string, string> modes = pr.GetProperty("steps").EnumerateArray()
            .ToDictionary(step => step.GetProperty("name").GetString()!, step => step.GetProperty("mode").GetString()!);
        Assert.Equal("report", modes["policy.protected"]);
        Assert.Equal("fail", modes["quality.suppressions"]);
        Assert.Contains("docs.references", modes.Keys);
        Assert.Equal("fail", modes["build"]);
        Assert.Equal("unit", pr.GetProperty("steps").EnumerateArray().Single(step => step.GetProperty("name").GetString() == "build").GetProperty("scope").GetString());
    }

    [Fact]
    public async Task AnalyzerBannedSymbolAndCodeMetricsSettingsComeFromTrackedFiles()
    {
        Write(".editorconfig", "[*.cs]\ndotnet_diagnostic.ca1502.severity = warning\ndotnet_diagnostic.IDE0005.severity = none\n");
        Write("src/BannedSymbols.txt", "P:System.DateTime.UtcNow; use TimeProvider\n// a comment\nT:System.Random\n");
        Write("CodeMetricsConfig.txt", "CA1502(Method): 15\r\nCA1505: 10\r\n");
        Write("untracked/.editorconfig", "dotnet_diagnostic.CA2000.severity = error\n");
        await CommitAsync(exclude: "untracked");

        using JsonDocument facts = await FactsAsync();

        JsonElement[] analyzers = [.. facts.RootElement.GetProperty("analyzers").EnumerateArray()];
        Assert.Equal(["CA1502", "IDE0005"], analyzers.Select(setting => setting.GetProperty("id").GetString()));
        Assert.True(analyzers[0].GetProperty("reachesBuild").GetBoolean());
        Assert.False(analyzers[1].GetProperty("reachesBuild").GetBoolean());
        JsonElement banned = Assert.Single(facts.RootElement.GetProperty("bannedSymbols").EnumerateArray());
        Assert.Equal(["P:System.DateTime.UtcNow", "T:System.Random"], banned.GetProperty("symbols").EnumerateArray().Select(symbol => symbol.GetString()));
        JsonElement[] metrics = [.. facts.RootElement.GetProperty("codeMetrics").EnumerateArray()];
        Assert.Equal(15, metrics[0].GetProperty("threshold").GetInt32());
        Assert.Equal("Method", metrics[0].GetProperty("target").GetString());
        Assert.Equal(JsonValueKind.Null, metrics[1].GetProperty("target").ValueKind);
    }

    [Fact]
    public async Task InstructionFilesArchitectureTestsAndTheLedgerAreListed()
    {
        Write(Manifest.FileName, "version: 1\n");
        Write("CLAUDE.md", "- Never edit generated code.\n");
        Write(".claude/skills/release/SKILL.md", "- Always tag.\n");
        Write("docs/guide.md", "- Not an instruction file.\n");
        Write("tests/Shop.ArchitectureTests/LayerTests.cs", "using ArchUnitNET.Fluent;\n");
        Write(".lockflight/ledger/events-2026-10.jsonl", "{}\n");
        await CommitAsync(exclude: ".lockflight");

        using JsonDocument facts = await FactsAsync();

        Assert.Equal([".claude/skills/release/SKILL.md", "CLAUDE.md"], facts.RootElement.GetProperty("instructionFiles").EnumerateArray().Select(file => file.GetString()));
        JsonElement architecture = facts.RootElement.GetProperty("architectureTests");
        Assert.True(architecture.GetProperty("present").GetBoolean());
        JsonElement ledger = facts.RootElement.GetProperty("ledger");
        Assert.Equal(".lockflight/ledger", ledger.GetProperty("directory").GetString());
        Assert.Equal([".lockflight/ledger/events-2026-10.jsonl"], ledger.GetProperty("files").EnumerateArray().Select(file => file.GetString()));
        Assert.Contains("approve spec", facts.RootElement.GetProperty("humanOnlyCommands").EnumerateArray().Select(command => command.GetString()));
    }

    [Fact]
    public async Task TheSameTreeGivesTheSameBytes()
    {
        Write(Manifest.FileName, "version: 1\n");
        Write(".editorconfig", "dotnet_diagnostic.CA1506.severity = warning\n");
        await CommitAsync();

        (_, string first, _) = await TestConsole.RunAsync(["facts", "--json"], string.Empty, root.FullName);
        (_, string second, _) = await TestConsole.RunAsync(["facts", "--json"], string.Empty, root.FullName);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task OutsideAGitRepositoryItIsAConfigurationError()
    {
        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["facts", "--json"], string.Empty, root.FullName);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("not a git repository", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutJsonItPrintsASummary()
    {
        Write(Manifest.FileName, "version: 1\n");
        await CommitAsync();

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["facts"], string.Empty, root.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains("gates pr: policy.protected (report)", output, StringComparison.Ordinal);
        Assert.Contains("--json", output, StringComparison.Ordinal);
    }

    private async Task<JsonDocument> FactsAsync()
    {
        (ExitCode exitCode, string output, string error) = await TestConsole.RunAsync(["facts", "--json"], string.Empty, root.FullName);
        Assert.True(exitCode == ExitCode.Pass, error);
        return JsonDocument.Parse(output);
    }

    private void Write(string relativePath, string content)
    {
        string path = Path.Combine(root.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private async Task CommitAsync(string? exclude = null)
    {
        if (exclude is not null)
        {
            Write(".gitignore", $"{exclude}/\n");
        }

        await GitAsync(["init", "--quiet", "--initial-branch=main"]);
        await GitAsync(["add", "--all"]);
        await GitAsync(["-c", "user.name=Test", "-c", "user.email=test@example.com", "commit", "--quiet", "-m", "facts"]);
    }

    private async Task GitAsync(string[] arguments)
    {
        ProcessResult result = await ProcessRunner.RunAsync("git", arguments, root.FullName, TestContext.Current.CancellationToken);
        Assert.True(result.ExitCode == 0, result.Combined);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);
}
