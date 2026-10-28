using System.Text.Json.Nodes;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Init;

namespace Lockflight.Cli.Tests.Init;

public sealed class InitCommandTests : IDisposable
{
    private readonly DirectoryInfo projectRoot = Directory.CreateTempSubdirectory("lockflight-init-");

    [Fact]
    public async Task InitOnAnEmptyRepositoryCreatesTheLiteProfile()
    {
        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["init", "--no-link"], "", projectRoot.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Equal(ManifestLoadStatus.Loaded, ManifestLocator.Discover(projectRoot.FullName).Status);
        Assert.Equal(ToolPin.RunningVersion, ToolPin.ReadPinnedVersion(projectRoot.FullName));
        Assert.True(File.Exists(Path.Combine(projectRoot.FullName, ".github", "workflows", "lockflight-gates.yml")));
        Assert.Contains("created", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheWorkflowInstallsEveryRuntimeTheTestProjectsTarget()
    {
        string project = Path.Combine(projectRoot.FullName, "test", "Parser.Tests", "Parser.Tests.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(project)!);
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFrameworks>net8.0;net10.0</TargetFrameworks></PropertyGroup>
              <ItemGroup><PackageReference Include="xunit.v3.mtp-v2" Version="4.0.2" /></ItemGroup>
            </Project>
            """);

        (_, string output, _) = await TestConsole.RunAsync(["init", "--no-link"], "", projectRoot.FullName);

        string workflow = File.ReadAllText(Path.Combine(projectRoot.FullName, ".github", "workflows", "lockflight-gates.yml"));
        Assert.Contains("dotnet-version: |\n            8.0.x\n            10.0.x\n", workflow, StringComparison.Ordinal);
        Assert.Contains("CI installs .NET 8.0.x, 10.0.x: the test projects target net10.0, net8.0", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnExistingConfigToolManifestGetsThePinInsteadOfASecondManifest()
    {
        Directory.CreateDirectory(Path.Combine(projectRoot.FullName, ".config"));
        File.WriteAllText(Path.Combine(projectRoot.FullName, ".config", "dotnet-tools.json"), """{ "version": 1, "isRoot": true, "tools": {} }""");

        (_, string output, _) = await TestConsole.RunAsync(["init", "--no-link"], "", projectRoot.FullName);

        Assert.False(File.Exists(Path.Combine(projectRoot.FullName, "dotnet-tools.json")));
        Assert.Equal(ToolPin.RunningVersion, ToolPin.ReadPinnedVersion(projectRoot.FullName));
        Assert.Contains("updated    .config/dotnet-tools.json", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANewerPinIsNeverLoweredAndNothingIsWritten()
    {
        const string tools = """{ "version": 1, "isRoot": true, "tools": { "lockflight.cli": { "version": "99.0.0", "commands": ["lockflight"] } } }""";
        File.WriteAllText(Path.Combine(projectRoot.FullName, "dotnet-tools.json"), tools);

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["init", "--no-link"], "", projectRoot.FullName);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("99.0.0", error, StringComparison.Ordinal);
        Assert.Contains("newer", error, StringComparison.Ordinal);
        Assert.Equal(tools, File.ReadAllText(Path.Combine(projectRoot.FullName, "dotnet-tools.json")));
        Assert.False(File.Exists(Path.Combine(projectRoot.FullName, Manifest.FileName)));
    }

    [Fact]
    public async Task AnOlderPinUnderAnotherCaseIsUpdatedInPlace()
    {
        File.WriteAllText(
            Path.Combine(projectRoot.FullName, "dotnet-tools.json"),
            """{ "version": 1, "isRoot": true, "tools": { "Lockflight.Cli": { "version": "0.0.0-dev.1", "commands": ["lockflight"] } } }""");

        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["init", "--no-link"], "", projectRoot.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
        JsonObject tools = JsonNode.Parse(File.ReadAllText(Path.Combine(projectRoot.FullName, "dotnet-tools.json")))!["tools"]!.AsObject();
        KeyValuePair<string, JsonNode?> pin = Assert.Single(tools);
        Assert.Equal("Lockflight.Cli", pin.Key);
        Assert.Equal(ToolPin.RunningVersion, pin.Value!["version"]!.GetValue<string>());
    }

    [Fact]
    public async Task APinThatIsNotAStringIsRefusedBeforeAnythingIsWritten()
    {
        File.WriteAllText(
            Path.Combine(projectRoot.FullName, "dotnet-tools.json"), """{ "version": 1, "isRoot": true, "tools": { "lockflight.cli": { "version": 1 } } }""");

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["init", "--no-link"], "", projectRoot.FullName);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("dotnet-tools.json", error, StringComparison.Ordinal);
        Assert.DoesNotContain("internal error", error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(projectRoot.FullName, Manifest.FileName)));
    }

    [Theory]
    [InlineData("// team settings\n{}")]
    [InlineData("""{ "enabledPlugins": { "lockflight@lockflight": "yes" } }""")]
    [InlineData("""{ "enabledPlugins": [] }""")]
    [InlineData("""{ "sandbox": { "filesystem": { "denyWrite": "./lockflight.yaml" } } }""")]
    [InlineData("""{ "permissions": { "deny": "Edit(/x)" } }""")]
    public async Task ClaudeSettingsInitCannotMergeIntoAreRefusedBeforeAnythingIsWritten(string settings)
    {
        Directory.CreateDirectory(Path.Combine(projectRoot.FullName, ".claude"));
        File.WriteAllText(Path.Combine(projectRoot.FullName, ".claude", "settings.json"), settings);

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["init", "--no-link"], "", projectRoot.FullName);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains(".claude/settings.json", error, StringComparison.Ordinal);
        Assert.Equal(settings, File.ReadAllText(Path.Combine(projectRoot.FullName, ".claude", "settings.json")));
        Assert.False(File.Exists(Path.Combine(projectRoot.FullName, Manifest.FileName)));
        Assert.False(File.Exists(Path.Combine(projectRoot.FullName, "dotnet-tools.json")));
    }

    [Fact]
    public async Task ASingleTargetKeepsTheEnginesChannelOnly()
    {
        await TestConsole.RunAsync(["init", "--no-link"], "", projectRoot.FullName);

        string workflow = File.ReadAllText(Path.Combine(projectRoot.FullName, ".github", "workflows", "lockflight-gates.yml"));
        Assert.Contains("dotnet-version: 10.0.x\n", workflow, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new[] { "net8.0", "net10.0-windows", "net48", "netstandard2.0" }, new[] { "8.0.x", "10.0.x" })]
    [InlineData(new[] { "netcoreapp3.1", "net9.0" }, new[] { "3.1.x", "9.0.x", "10.0.x" })]
    [InlineData(new string[0], new[] { "10.0.x" })]
    public void ChannelsCoverEveryModernTargetAndTheEngine(string[] frameworks, string[] expected) =>
        Assert.Equal(expected, DotnetChannels.For(frameworks));

    [Fact]
    public async Task InitInAWorkspaceShellListsTheUnitsAndIgnoresTheChildRepositories()
    {
        Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "services", "orders", ".git"));
        File.WriteAllText(Path.Combine(projectRoot.FullName, "services", "orders", "Orders.sln"), string.Empty);
        Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "web", "admin", ".git"));
        File.WriteAllText(Path.Combine(projectRoot.FullName, "web", "admin", "package.json"), "{}");
        File.WriteAllText(Path.Combine(projectRoot.FullName, ".gitignore"), "web/\n");

        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["init", "--no-link"], "", projectRoot.FullName);
        Manifest manifest = ManifestLocator.Discover(projectRoot.FullName).Manifest!;
        string gitIgnore = File.ReadAllText(Path.Combine(projectRoot.FullName, ".gitignore"));

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Equal([("services/orders", UnitKind.Auto), ("web/admin", UnitKind.None)], manifest.Units.Select(unit => (unit.Path, unit.Kind)));
        Assert.Contains("services/orders/", gitIgnore, StringComparison.Ordinal);
        Assert.DoesNotContain("web/admin/", gitIgnore, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARepositoryThatBuildsAtItsRootIsOneUnitEvenWithAWebFolder()
    {
        File.WriteAllText(Path.Combine(projectRoot.FullName, "App.slnx"), "<Solution />");
        Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "web"));
        File.WriteAllText(Path.Combine(projectRoot.FullName, "web", "package.json"), "{}");
        Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "tools", "Gen"));
        File.WriteAllText(Path.Combine(projectRoot.FullName, "tools", "Gen", "Gen.sln"), string.Empty);

        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["init", "--no-link"], "", projectRoot.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Empty(ManifestLocator.Discover(projectRoot.FullName).Manifest!.Units);
    }

    [Fact]
    public async Task ManualCiWritesAWorkflowThatRunsOnlyWhenStartedByHand()
    {
        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["init", "--no-link", "--ci", "manual"], "", projectRoot.FullName);

        string workflow = File.ReadAllText(Path.Combine(projectRoot.FullName, ".github", "workflows", "lockflight-gates.yml"));
        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains("on:\n  workflow_dispatch:\n", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("  pull_request:\n", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APublishedEngineWritesAWorkflowThatJudgesEveryPullRequestByDefault()
    {
        Assert.False(EngineVersion.IsDevelopmentBuild(ToolPin.RunningVersion));

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["init", "--no-link"], "", projectRoot.FullName);

        string workflow = File.ReadAllText(Path.Combine(projectRoot.FullName, ".github", "workflows", "lockflight-gates.yml"));
        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains("on:\n  pull_request:\n", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("development build", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoCiWritesNoWorkflow()
    {
        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["init", "--no-link", "--ci", "none"], "", projectRoot.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.False(File.Exists(Path.Combine(projectRoot.FullName, ".github", "workflows", "lockflight-gates.yml")));
        Assert.DoesNotContain("lockflight-gates.yml", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--ci")]
    [InlineData("--ci", "nightly")]
    public async Task AMissingOrUnknownCiTriggerIsAUsageError(params string[] options)
    {
        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["init", "--no-link", .. options], "", projectRoot.FullName);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("pr, manual or none", error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(projectRoot.FullName, "lockflight.yaml")));
    }

    [Fact]
    public async Task AzureGetsAPipelineOfItsOwnAndNoGitHubWorkflow()
    {
        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["init", "--no-link", "--ci-platform", "azure"], "", projectRoot.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.True(File.Exists(Path.Combine(projectRoot.FullName, AzurePipelinesTemplate.Path)));
        Assert.False(File.Exists(Path.Combine(projectRoot.FullName, Templates.WorkflowPath)));
        Assert.False(File.Exists(Path.Combine(projectRoot.FullName, "azure-pipelines.yml")));
        Assert.Contains("build validation policy", output, StringComparison.Ordinal);
        Assert.Contains("Edit(/.azure-pipelines/lockflight-gates.yml)", File.ReadAllText(Path.Combine(projectRoot.FullName, ".claude", "settings.json")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GitLabGetsTheJobAndARootPipelineThatIncludesIt()
    {
        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["init", "--no-link", "--ci-platform", "gitlab"], "", projectRoot.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.True(File.Exists(Path.Combine(projectRoot.FullName, GitLabCiTemplate.Path)));
        Assert.True(GitLabCiTemplate.IsIncludedBy(File.ReadAllText(Path.Combine(projectRoot.FullName, GitLabCiTemplate.RootPath))));
        string settings = File.ReadAllText(Path.Combine(projectRoot.FullName, ".claude", "settings.json"));
        Assert.Contains("Edit(/.gitlab/ci/lockflight-gates.yml)", settings, StringComparison.Ordinal);
        Assert.Contains("Edit(/.gitlab-ci.yml)", settings, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnExistingGitLabPipelineIsKeptAndInitSaysHowToIncludeTheJob()
    {
        const string teamPipeline = "build:\n  script: [make]\n";
        File.WriteAllText(Path.Combine(projectRoot.FullName, GitLabCiTemplate.RootPath), teamPipeline);

        (_, string output, _) = await TestConsole.RunAsync(["init", "--no-link", "--ci-platform", "gitlab"], "", projectRoot.FullName);
        (_, string again, _) = await TestConsole.RunAsync(["init", "--no-link", "--ci-platform", "gitlab"], "", projectRoot.FullName);

        Assert.Equal(teamPipeline, File.ReadAllText(Path.Combine(projectRoot.FullName, GitLabCiTemplate.RootPath)));
        Assert.True(File.Exists(Path.Combine(projectRoot.FullName, GitLabCiTemplate.Path)));
        Assert.Contains("- local: /" + GitLabCiTemplate.Path, output, StringComparison.Ordinal);
        Assert.Contains("- local: /" + GitLabCiTemplate.Path, again, StringComparison.Ordinal);
        Assert.DoesNotContain("created", again, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BitbucketGetsThePipelineFileWhenThereIsNone()
    {
        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["init", "--no-link", "--ci-platform", "bitbucket"], "", projectRoot.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains(CiShellScripts.GateCommand, File.ReadAllText(Path.Combine(projectRoot.FullName, BitbucketPipelinesTemplate.Path)), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(projectRoot.FullName, BitbucketPipelinesTemplate.FragmentPath)));
        Assert.Contains("Premium", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnExistingBitbucketPipelineIsKeptAndTheJobGoesToACopyToMerge()
    {
        const string teamPipeline = "pipelines:\n  default:\n    - step:\n        script: [make]\n";
        File.WriteAllText(Path.Combine(projectRoot.FullName, BitbucketPipelinesTemplate.Path), teamPipeline);

        (_, string output, _) = await TestConsole.RunAsync(["init", "--no-link", "--ci-platform", "bitbucket"], "", projectRoot.FullName);
        (_, string again, _) = await TestConsole.RunAsync(["init", "--no-link", "--ci-platform", "bitbucket"], "", projectRoot.FullName);

        Assert.Equal(teamPipeline, File.ReadAllText(Path.Combine(projectRoot.FullName, BitbucketPipelinesTemplate.Path)));
        Assert.Contains(CiShellScripts.GateCommand, File.ReadAllText(Path.Combine(projectRoot.FullName, BitbucketPipelinesTemplate.FragmentPath)), StringComparison.Ordinal);
        Assert.Contains($"merge the lockflight-gates step and its pipelines from {BitbucketPipelinesTemplate.FragmentPath}", output, StringComparison.Ordinal);
        Assert.DoesNotContain("created", again, StringComparison.Ordinal);
        Assert.DoesNotContain("CI:", again, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("azure")]
    [InlineData("bitbucket")]
    [InlineData("gitlab")]
    public async Task InitTwiceOnAnotherPlatformChangesNothingTheSecondTime(string platform)
    {
        await TestConsole.RunAsync(["init", "--no-link", "--ci-platform", platform], "", projectRoot.FullName);

        (_, string output, _) = await TestConsole.RunAsync(["init", "--no-link", "--ci-platform", platform], "", projectRoot.FullName);

        Assert.DoesNotContain("created", output, StringComparison.Ordinal);
        Assert.DoesNotContain("updated", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoCiOnAnotherPlatformWritesNoPipeline()
    {
        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["init", "--no-link", "--ci", "none", "--ci-platform", "gitlab"], "", projectRoot.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.False(File.Exists(Path.Combine(projectRoot.FullName, GitLabCiTemplate.RootPath)));
        Assert.False(File.Exists(Path.Combine(projectRoot.FullName, GitLabCiTemplate.Path)));
    }

    [Theory]
    [InlineData("--ci-platform")]
    [InlineData("--ci-platform", "jenkins")]
    public async Task AMissingOrUnknownCiPlatformIsAUsageError(params string[] options)
    {
        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["init", "--no-link", .. options], "", projectRoot.FullName);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains(CiPlatformNames.Choices, error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(projectRoot.FullName, "lockflight.yaml")));
    }

    [Fact]
    public async Task InitTwiceChangesNothingTheSecondTime()
    {
        await TestConsole.RunAsync(["init", "--no-link"], "", projectRoot.FullName);

        (_, string output, _) = await TestConsole.RunAsync(["init", "--no-link"], "", projectRoot.FullName);

        Assert.DoesNotContain("created", output, StringComparison.Ordinal);
        Assert.DoesNotContain("updated", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgentWorktreesAreIgnored()
    {
        await TestConsole.RunAsync(["init", "--no-link"], "", projectRoot.FullName);

        string[] gitIgnore = File.ReadAllLines(Path.Combine(projectRoot.FullName, ".gitignore"));
        Assert.Contains(".claude/worktrees/", gitIgnore);
    }

    [Fact]
    public async Task ExistingClaudeSettingsAndGitIgnoreKeepTheirContent()
    {
        Directory.CreateDirectory(Path.Combine(projectRoot.FullName, ".claude"));
        File.WriteAllText(Path.Combine(projectRoot.FullName, ".claude", "settings.json"), """{ "permissions": { "deny": ["Read(./.env)"] } }""");
        File.WriteAllText(Path.Combine(projectRoot.FullName, ".gitignore"), "bin/\nobj/");

        await TestConsole.RunAsync(["init", "--no-link"], "", projectRoot.FullName);

        JsonNode settings = JsonNode.Parse(File.ReadAllText(Path.Combine(projectRoot.FullName, ".claude", "settings.json")))!;
        Assert.Equal("Read(./.env)", settings["permissions"]!["deny"]![0]!.GetValue<string>());
        Assert.Contains("Edit(/lockflight.yaml)", settings["permissions"]!["deny"]!.AsArray().Select(rule => rule!.GetValue<string>()));
        Assert.True(settings["enabledPlugins"]!["lockflight@lockflight"]!.GetValue<bool>());
        string gitIgnore = File.ReadAllText(Path.Combine(projectRoot.FullName, ".gitignore"));
        Assert.StartsWith("bin/\nobj/\n", gitIgnore, StringComparison.Ordinal);
        Assert.Contains(".lockflight/bin/", gitIgnore, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitListsTheProtectedPathsAsSandboxWriteDenialsWithoutTurningTheSandboxOn()
    {
        File.WriteAllText(Path.Combine(projectRoot.FullName, "global.json"), "{}");

        await TestConsole.RunAsync(["init", "--no-link"], "", projectRoot.FullName);

        JsonNode settings = JsonNode.Parse(File.ReadAllText(Path.Combine(projectRoot.FullName, ".claude", "settings.json")))!;
        string[] denied = [.. settings["sandbox"]!["filesystem"]!["denyWrite"]!.AsArray().Select(entry => entry!.GetValue<string>())];
        Assert.Contains("./lockflight.yaml", denied);
        Assert.Contains("./global.json", denied);
        Assert.Contains("./.lockflight/bin", denied);
        Assert.Contains("~/.nuget/packages", settings["sandbox"]!["filesystem"]!["allowWrite"]!.AsArray().Select(entry => entry!.GetValue<string>()));
        Assert.Null(settings["sandbox"]!["enabled"]);
    }

    [Fact]
    public async Task NoSandboxWritesNoSandboxSettings()
    {
        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["init", "--no-link", "--no-sandbox"], "", projectRoot.FullName);

        JsonNode settings = JsonNode.Parse(File.ReadAllText(Path.Combine(projectRoot.FullName, ".claude", "settings.json")))!;
        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Null(settings["sandbox"]);
    }

    [Fact]
    public async Task InitAgainAddsAProtectedFileCreatedSince()
    {
        await TestConsole.RunAsync(["init", "--no-link"], "", projectRoot.FullName);
        File.WriteAllText(Path.Combine(projectRoot.FullName, "Directory.Build.props"), "<Project />");

        (_, string output, _) = await TestConsole.RunAsync(["init", "--no-link"], "", projectRoot.FullName);

        Assert.Contains("updated    .claude/settings.json", output, StringComparison.Ordinal);
        Assert.Contains("./Directory.Build.props", File.ReadAllText(Path.Combine(projectRoot.FullName, ".claude", "settings.json")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitAgainAddsADenyRuleForAProtectedFileCreatedSince()
    {
        await TestConsole.RunAsync(["init", "--no-link"], "", projectRoot.FullName);
        File.WriteAllText(Path.Combine(projectRoot.FullName, "global.json"), "{}");

        (_, string output, _) = await TestConsole.RunAsync(["init", "--no-link"], "", projectRoot.FullName);

        Assert.Contains("updated    .claude/settings.json", output, StringComparison.Ordinal);
        Assert.Contains("Edit(/global.json)", File.ReadAllText(Path.Combine(projectRoot.FullName, ".claude", "settings.json")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DoctorBeforeLinkingReportsRecoveryModeWithTheFix()
    {
        await TestConsole.RunAsync(["init", "--no-link"], "", projectRoot.FullName);

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["doctor"], "", projectRoot.FullName);

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains("linked engine", output, StringComparison.Ordinal);
        Assert.Contains("dotnet tool restore && dotnet lockflight link", output, StringComparison.Ordinal);
    }

    public void Dispose() => projectRoot.Delete(recursive: true);
}
