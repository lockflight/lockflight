using System.Text.Json.Nodes;
using Lockflight.Cli.Adoption;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Init;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Init;

public sealed class InitInstallLevelTests : IDisposable
{
    private static readonly string[] HookEvents = ["SessionStart", "PreToolUse", "TaskCompleted", "Stop"];

    private readonly DirectoryInfo projectRoot = Directory.CreateTempSubdirectory("lockflight-init-level-");

    [Fact]
    public async Task NativeWritesHooksThatRunTheVendoredShimAndDoesNotEnableThePlugin()
    {
        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["init", "--no-link", "--native"], "", projectRoot.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
        JsonNode settings = Settings();
        Assert.Null(settings["enabledPlugins"]);
        Assert.Null(settings["extraKnownMarketplaces"]);
        foreach (string hookEvent in HookEvents)
        {
            string command = settings["hooks"]![hookEvent]![0]!["hooks"]![0]!["command"]!.GetValue<string>();
            Assert.Contains("LOCKFLIGHT_HOOK_ADAPTER=claude-code sh \"$CLAUDE_PROJECT_DIR/.claude/hooks/lockflight-hook\"", command, StringComparison.Ordinal);
        }

        Assert.Null(settings["hooks"]!["UserPromptExpansion"]);
        Assert.Equal("block", settings["hooks"]!["PreToolUse"]![0]!["hooks"]![0]!["onFailure"]!.GetValue<string>());
        Assert.Equal(".*", settings["hooks"]!["PreToolUse"]![0]!["matcher"]!.GetValue<string>());
        Assert.Equal(NativeHookShim.Text, File.ReadAllText(Path.Combine(projectRoot.FullName, ".claude", "hooks", "lockflight-hook")));
        Assert.Contains(GitAttributesUpdater.HookShimRule, File.ReadAllLines(Path.Combine(projectRoot.FullName, ".gitattributes")));
        Assert.Contains("Edit(/.claude/hooks/**)", settings["permissions"]!["deny"]!.AsArray().Select(rule => rule!.GetValue<string>()));
        Assert.Contains(ClaudeMdBlock.Begin, File.ReadAllText(Path.Combine(projectRoot.FullName, "CLAUDE.md")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("session-start", true)]
    [InlineData("stop", true)]
    [InlineData("pre-tool-use", false)]
    public async Task WithoutALinkedEngineTheNativeHooksSayTheChecksAreOff(string engineEvent, bool tells)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The hook command runs in a POSIX sh.");
        Dictionary<string, string?> environment = new() { ["CLAUDE_PROJECT_DIR"] = projectRoot.FullName };

        ProcessResult result = await ProcessRunner.RunAsync(
            "sh", ["-c", NativeHooks.Command(engineEvent)], projectRoot.FullName, Timeout.InfiniteTimeSpan, environment, TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        if (tells)
        {
            Assert.Equal(NativeHooks.NoEngineMessage, JsonNode.Parse(result.Output)!["systemMessage"]!.GetValue<string>());
        }
        else
        {
            Assert.Empty(result.Output);
        }
    }

    [Fact]
    public async Task NativeTwiceChangesNothingTheSecondTime()
    {
        await TestConsole.RunAsync(["init", "--no-link", "--native"], "", projectRoot.FullName);

        (_, string output, _) = await TestConsole.RunAsync(["init", "--no-link", "--native"], "", projectRoot.FullName);

        Assert.DoesNotContain("created", output, StringComparison.Ordinal);
        Assert.DoesNotContain("updated", output, StringComparison.Ordinal);
        Assert.Single(Settings()["hooks"]!["PreToolUse"]!.AsArray());
    }

    [Fact]
    public async Task NativeKeepsTheTeamsHooksAndBringsItsOwnUpToDate()
    {
        string teamHook = """{ "type": "command", "command": "./scripts/format.sh" }""";
        string staleLockflightHook = """{ "type": "command", "command": "sh \"$CLAUDE_PROJECT_DIR/.claude/hooks/lockflight-hook\" stop", "timeout": 5 }""";
        WriteSettings($$"""{ "hooks": { "PreToolUse": [ { "matcher": "Edit", "hooks": [ {{teamHook}} ] } ], "Stop": [ { "hooks": [ {{staleLockflightHook}}, {{teamHook}} ] } ] }, "model": "sonnet" }""");

        await TestConsole.RunAsync(["init", "--no-link", "--native"], "", projectRoot.FullName);

        JsonNode settings = Settings();
        Assert.Equal("sonnet", settings["model"]!.GetValue<string>());
        JsonArray preToolUse = settings["hooks"]!["PreToolUse"]!.AsArray();
        Assert.Equal(2, preToolUse.Count);
        Assert.Equal("./scripts/format.sh", preToolUse[0]!["hooks"]![0]!["command"]!.GetValue<string>());
        JsonArray stop = settings["hooks"]!["Stop"]!.AsArray();
        Assert.Equal(2, stop.Count);
        Assert.Equal("./scripts/format.sh", Assert.Single(stop[0]!["hooks"]!.AsArray())!["command"]!.GetValue<string>());
        JsonNode lockflightHook = Assert.Single(stop[1]!["hooks"]!.AsArray())!;
        Assert.Equal(NativeHooks.Command("stop"), lockflightHook["command"]!.GetValue<string>());
        Assert.Equal(600, lockflightHook["timeout"]!.GetValue<int>());
        Assert.True(NativeHooks.AreCurrent(settings));
    }

    [Fact]
    public async Task NativeRestoresANarrowedMatcherAndRemovesADuplicate()
    {
        await TestConsole.RunAsync(["init", "--no-link", "--native"], "", projectRoot.FullName);
        JsonNode settings = Settings();
        settings["hooks"]!["PreToolUse"]![0]!["matcher"] = "Edit";
        settings["hooks"]!["PreToolUse"]!.AsArray().Add((JsonNode)JsonNode.Parse(settings["hooks"]!["PreToolUse"]![0]!.ToJsonString())!);
        WriteSettings(settings.ToJsonString());
        Assert.False(NativeHooks.AreCurrent(Settings()));

        await TestConsole.RunAsync(["init", "--no-link", "--native"], "", projectRoot.FullName);

        JsonNode merged = Settings();
        Assert.Equal(".*", Assert.Single(merged["hooks"]!["PreToolUse"]!.AsArray())!["matcher"]!.GetValue<string>());
        Assert.True(NativeHooks.AreCurrent(merged));
    }

    [Fact]
    public void SettingsOfAnotherShapeHoldNoNativeHooks()
    {
        Assert.False(NativeHooks.AnyPresent(JsonNode.Parse("""{ "hooks": [] }""")));
        Assert.False(NativeHooks.AnyPresent(JsonNode.Parse("""{ "hooks": { "Stop": "x" } }""")));
        Assert.False(NativeHooks.AnyPresent(JsonNode.Parse("[]")));
        Assert.False(ClaudeSettingsMerger.PluginEnabled(JsonNode.Parse("""{ "enabledPlugins": [] }""")));
    }

    [Theory]
    [InlineData("""{ "hooks": [] }""")]
    [InlineData("""{ "hooks": { "Stop": {} } }""")]
    [InlineData("""{ "hooks": { "Stop": [ { "hooks": [ "echo" ] } ] } }""")]
    public async Task NativeRefusesHooksItCannotMergeIntoBeforeAnythingIsWritten(string settings)
    {
        WriteSettings(settings);

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["init", "--no-link", "--native"], "", projectRoot.FullName);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("hooks", error, StringComparison.Ordinal);
        Assert.Equal(settings, File.ReadAllText(Path.Combine(projectRoot.FullName, ".claude", "settings.json")));
        Assert.False(File.Exists(Path.Combine(projectRoot.FullName, "lockflight.yaml")));
    }

    [Fact]
    public async Task ThePluginLevelIgnoresAHooksKeyItDoesNotWrite()
    {
        WriteSettings("""{ "hooks": [] }""");

        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["init", "--no-link"], "", projectRoot.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.True(Settings()["enabledPlugins"]!["lockflight@lockflight"]!.GetValue<bool>());
    }

    [Fact]
    public async Task CiOnlyWritesNothingForClaudeCode()
    {
        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["init", "--ci-only"], "", projectRoot.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.True(File.Exists(Path.Combine(projectRoot.FullName, "lockflight.yaml")));
        Assert.True(File.Exists(Path.Combine(projectRoot.FullName, ".github", "workflows", "lockflight-gates.yml")));
        Assert.False(File.Exists(Path.Combine(projectRoot.FullName, ".claude", "settings.json")));
        Assert.False(File.Exists(Path.Combine(projectRoot.FullName, "CLAUDE.md")));
        Assert.False(Directory.Exists(Path.Combine(projectRoot.FullName, ".lockflight", "bin")));
    }

    [Theory]
    [InlineData("--native", "--ci-only")]
    [InlineData("--ci-only", "--ci", "none")]
    public async Task ConflictingLevelOptionsAreRefusedBeforeAnythingIsWritten(params string[] options)
    {
        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["init", "--no-link", .. options], "", projectRoot.FullName);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.False(File.Exists(Path.Combine(projectRoot.FullName, "lockflight.yaml")));
    }

    [Fact]
    public async Task ANativeHookWithNoEngineLinkedAllowsTheCall()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the hook command runs under a POSIX shell");
        await TestConsole.RunAsync(["init", "--no-link", "--native"], "", projectRoot.FullName);
        string command = Settings()["hooks"]!["PreToolUse"]![0]!["hooks"]![0]!["command"]!.GetValue<string>();

        ProcessResult result = await ProcessRunner.RunAsync(
            "sh", ["-c", $"CLAUDE_PROJECT_DIR=\"$0\"; export CLAUDE_PROJECT_DIR; {command}", projectRoot.FullName], projectRoot.FullName, TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task ANativeHookInLockdownWithNoEngineRunsTheShimWhichAllowsOnlyReading()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the hook command runs under a POSIX shell");
        await TestConsole.RunAsync(["init", "--no-link", "--native"], "", projectRoot.FullName);
        File.WriteAllText(Path.Combine(projectRoot.FullName, ".lockflight", "lockdown"), "");
        string command = Settings()["hooks"]!["PreToolUse"]![0]!["hooks"]![0]!["command"]!.GetValue<string>();
        string payload = Path.Combine(projectRoot.FullName, "payload.json");
        File.WriteAllText(payload, $$"""{ "tool_name": "Write", "tool_input": { "file_path": "src/A.cs", "content": "x" }, "cwd": "{{projectRoot.FullName.Replace("\\", "/")}}" }""");

        ProcessResult result = await ProcessRunner.RunAsync(
            "sh", ["-c", $"CLAUDE_PROJECT_DIR=\"$0\"; export CLAUDE_PROJECT_DIR; {{ {command}; }} < \"$1\"", projectRoot.FullName, payload], projectRoot.FullName, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public void TheEmbeddedShimIsThePluginsShim()
    {
        string pluginShim = File.ReadAllText(Path.Combine(RepositoryRoot(), "plugins", "lockflight", "bin", "lockflight-hook"));

        Assert.Equal(pluginShim, NativeHookShim.Text);
    }

    public void Dispose() => projectRoot.Delete(recursive: true);

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "plugins", "lockflight")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("the repository root was not found above the test binaries");
    }

    private JsonNode Settings() => JsonNode.Parse(File.ReadAllText(Path.Combine(projectRoot.FullName, ".claude", "settings.json")))!;

    private void WriteSettings(string settings)
    {
        Directory.CreateDirectory(Path.Combine(projectRoot.FullName, ".claude"));
        File.WriteAllText(Path.Combine(projectRoot.FullName, ".claude", "settings.json"), settings);
    }
}
