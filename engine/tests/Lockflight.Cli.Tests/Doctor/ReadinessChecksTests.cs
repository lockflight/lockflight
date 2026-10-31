using System.Text.Json.Nodes;
using Lockflight.Cli.Config;
using Lockflight.Cli.Doctor;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Init;
using Lockflight.Cli.Linking;

namespace Lockflight.Cli.Tests.Doctor;

public sealed class ReadinessChecksTests : IDisposable
{
    private const string DevelopmentVersion = "0.2.0-dev.20260101000000";

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-doctor-");

    [Fact]
    public async Task AStandardProfileWithoutTheLockflightPresetFails()
    {
        Write(".gitattributes", GitAttributesUpdater.Rule + "\n");
        Directory.CreateDirectory(Path.Combine(root.FullName, ".specify"));

        DoctorCheck preset = await CheckAsync("lockflight preset");

        Assert.Equal(CheckStatus.Fail, preset.Status);
        Assert.Contains("specify preset add", preset.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStandardProfileWithoutSpecKitReportsItAsInformationAndNoSpecKitChecks()
    {
        Write(".gitattributes", GitAttributesUpdater.Rule + "\n");

        IReadOnlyList<DoctorCheck> checks = await ReadinessChecks.RunAsync(root.FullName, userHome: null, nugetPackages: null, gitleaks: null, EnvironmentReader.Of(new Dictionary<string, string>()), TimeProvider.System, TestContext.Current.CancellationToken);

        DoctorCheck preset = Assert.Single(checks, check => check.Name == "lockflight preset");
        Assert.Equal(CheckStatus.Info, preset.Status);
        Assert.Contains("optional", preset.Detail, StringComparison.Ordinal);
        Assert.Null(preset.Fix);
        Assert.DoesNotContain(checks, check => check.Name == "spec kit presets");
        Assert.Contains(checks, check => check.Name == "claude cli");
    }

    [Fact]
    public async Task AnInstalledPresetInSpecKitsRegistryPasses()
    {
        Write(".gitattributes", GitAttributesUpdater.Rule + "\n");
        Write(".specify/presets/.registry", """{ "schema_version": "1.0", "presets": { "lockflight": { "version": "0.1.0", "enabled": true, "priority": 10 } } }""");

        DoctorCheck preset = await CheckAsync("lockflight preset");

        Assert.Equal(CheckStatus.Pass, preset.Status);
        Assert.Contains("0.1.0", preset.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProjectThatUsesTheLockflightPresetGetsTheStandardChecksWithoutTheGitAttributesRule()
    {
        // init --speckit-preset installs the preset into a lite project, which has no .gitattributes rule.
        Write(".specify/presets/.registry", """{ "presets": { "lockflight": { "version": "0.1.0", "enabled": true } } }""");

        IReadOnlyList<DoctorCheck> checks = await ReadinessChecks.RunAsync(root.FullName, userHome: null, nugetPackages: null, gitleaks: null, EnvironmentReader.Of(new Dictionary<string, string>()), TimeProvider.System, TestContext.Current.CancellationToken);

        Assert.Contains(checks, check => check is { Name: "lockflight preset", Status: CheckStatus.Pass });
        Assert.Contains(checks, check => check.Name == "spec kit presets");
        Assert.Contains(checks, check => check.Name == "claude cli");
    }

    [Fact]
    public async Task ALiteProjectsOwnSpecKitGetsNoPresetChecks()
    {
        Write(".specify/presets/.registry", """{ "presets": { "lockflight": { "version": "0.1.0", "enabled": false } } }""");

        IReadOnlyList<DoctorCheck> checks = await ReadinessChecks.RunAsync(root.FullName, userHome: null, nugetPackages: null, gitleaks: null, EnvironmentReader.Of(new Dictionary<string, string>()), TimeProvider.System, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(checks, check => check.Name is "lockflight preset" or "spec kit presets");
    }

    [Fact]
    public async Task ACoverageCollectorWithoutItsPackageInATestProjectFails()
    {
        Write(Manifest.FileName, "version: 1\ngates:\n  coverage:\n    collector: microsoft\n");
        Write("global.json", """{ "test": { "runner": "Microsoft.Testing.Platform" } }""");
        Write("tests/A.Tests/A.Tests.csproj", """<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="xunit.v3.mtp-v2" Version="4.0.2" /></ItemGroup></Project>""");
        Write("tests/B.Tests/B.Tests.csproj", """<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="xunit.v3.mtp-v2" Version="4.0.2" /></ItemGroup></Project>""");
        Write("tests/B.Tests/Directory.Build.props", """<Project><ItemGroup><PackageReference Include="Microsoft.Testing.Extensions.CodeCoverage" Version="18.0.4" /></ItemGroup></Project>""");

        DoctorCheck coverage = await CheckAsync("coverage collector");

        Assert.Equal(CheckStatus.Fail, coverage.Status);
        Assert.Contains("Microsoft.Testing.Extensions.CodeCoverage", coverage.Detail, StringComparison.Ordinal);
        Assert.Contains("tests/A.Tests/A.Tests.csproj", coverage.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("B.Tests", coverage.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnVSTestTheCoverletCollectorIsTheExpectedPackage()
    {
        Write(Manifest.FileName, "version: 1\ngates:\n  coverage:\n    collector: coverlet\n");
        Write("tests/A.Tests/A.Tests.csproj", """<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="xunit" Version="2.9.2" /><PackageReference Include="coverlet.collector" Version="6.0.2" /></ItemGroup></Project>""");
        Write("tests/B.Tests/B.Tests.csproj", """<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="xunit" Version="2.9.2" /></ItemGroup></Project>""");

        DoctorCheck coverage = await CheckAsync("coverage collector");

        Assert.Equal(CheckStatus.Fail, coverage.Status);
        Assert.Contains("tests/B.Tests/B.Tests.csproj (coverlet.collector)", coverage.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("A.Tests", coverage.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoCoverageCheckRunsWithoutACollector()
    {
        Write(Manifest.FileName, "version: 1\n");

        IReadOnlyList<DoctorCheck> checks = await ReadinessChecks.RunAsync(root.FullName, userHome: null, nugetPackages: null, gitleaks: null, EnvironmentReader.Of(new Dictionary<string, string>()), TimeProvider.System, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(checks, check => check.Name == "coverage collector");
    }

    [Fact]
    public async Task AMissingPythonIsAWarningWithItsFixNotACrash()
    {
        DoctorCheck check = await ReadinessChecks.CheckPyYamlAsync(root.FullName, "lockflight-no-such-python", TestContext.Current.CancellationToken);

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Contains("python3 not found", check.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void GitleaksMissingWarnsThatThePrSecretsStepIsNotEarned()
    {
        DoctorCheck check = ReadinessChecks.CheckGitleaks(gitleaks: null);

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Contains("NOT-EARNED", check.Detail, StringComparison.Ordinal);
        Assert.Contains("LOCKFLIGHT_GITLEAKS", check.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void GitleaksFoundPasses() =>
        Assert.Equal(CheckStatus.Pass, ReadinessChecks.CheckGitleaks("/usr/local/bin/gitleaks").Status);

    [Theory]
    [InlineData("2.1.295 (Claude Code)", true)]
    [InlineData("2.1.309 (Claude Code)\n", true)]
    [InlineData("2.2.0", true)]
    [InlineData("2.1.294 (Claude Code)", false)]
    [InlineData("1.0.128 (Claude Code)", false)]
    [InlineData("Claude Code", false)]
    [InlineData(null, false)]
    public void ClaudeCodeOlderThanTheHooksNeedOrOfUnknownVersionWarns(string? versionOutput, bool recentEnough)
    {
        DoctorCheck check = ReadinessChecks.CheckClaudeVersion(versionOutput);

        Assert.Equal(recentEnough ? CheckStatus.Pass : CheckStatus.Warn, check.Status);
        if (!recentEnough)
        {
            Assert.Contains("2.1.295", check.Detail, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AMissingClaudeCodeIsAWarningNotACrash()
    {
        DoctorCheck check = await ReadinessChecks.CheckClaudeVersionAsync(root.FullName, "lockflight-no-such-claude", TestContext.Current.CancellationToken);

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Contains("not on PATH", check.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingBashFailsWithItsFixNotACrash()
    {
        DoctorCheck check = await ReadinessChecks.CheckGitBashAsync(root.FullName, "lockflight-no-such-bash", TestContext.Current.CancellationToken);

        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Contains("Git for Windows", check.Fix, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("// team settings\n{ \"enabledPlugins\": {} }")]
    [InlineData("""{ "enabledPlugins": { "lockflight@lockflight": "yes" } }""")]
    [InlineData("""{ "enabledPlugins": [] }""")]
    [InlineData("""{ "extraKnownMarketplaces": "lockflight" }""")]
    [InlineData("""[]""")]
    public async Task ClaudeSettingsThatCannotBeReadFailWithTheirFix(string settings)
    {
        Write(".claude/settings.json", settings);

        DoctorCheck check = await CheckAsync("claude settings");

        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Contains(".claude/settings.json", check.Detail, StringComparison.Ordinal);
        Assert.Contains("init", check.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClaudeSettingsWithThePluginDisabledWarn()
    {
        Write(".claude/settings.json", """{ "enabledPlugins": { "lockflight@lockflight": false }, "extraKnownMarketplaces": { "lockflight": {} } }""");

        Assert.Equal(CheckStatus.Warn, (await CheckAsync("claude settings")).Status);
    }

    [Fact]
    public async Task AfterInitNativeTheClaudeSettingsPassAtTheNativeLevel()
    {
        await TestConsole.RunAsync(["init", "--no-link", "--native"], "", root.FullName);

        DoctorCheck check = await CheckAsync("claude settings");

        Assert.Equal(CheckStatus.Pass, check.Status);
        Assert.Contains("native level", check.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NativeHooksWithThePluginAlsoEnabledWarnThatEverythingRunsTwice()
    {
        await TestConsole.RunAsync(["init", "--no-link", "--native"], "", root.FullName);
        JsonNode settings = JsonNode.Parse(File.ReadAllText(Path.Combine(root.FullName, ".claude", "settings.json")))!;
        settings["enabledPlugins"] = new JsonObject { ["lockflight@lockflight"] = true };
        Write(".claude/settings.json", settings.ToJsonString());

        DoctorCheck check = await CheckAsync("claude settings");

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Contains("twice", check.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEditedNativeHookShimWarnsWithInitNativeAsTheFix()
    {
        await TestConsole.RunAsync(["init", "--no-link", "--native"], "", root.FullName);
        Write(".claude/hooks/lockflight-hook", "exit 0\n");

        DoctorCheck check = await CheckAsync("claude settings");

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Contains("init --native", check.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AfterInitEveryProtectedPathHasAnEditDenyRuleAndASandboxWriteDenial()
    {
        await InitAsync();

        Assert.Equal(CheckStatus.Pass, (await CheckAsync("deny rules")).Status);
        Assert.Equal(CheckStatus.Pass, (await CheckAsync("sandbox")).Status);
    }

    [Fact]
    public async Task AnEditDenyRuleRemovedAfterInitIsNamedWithTheFix()
    {
        await InitAsync();
        RemoveFromSettings("permissions", "deny", "Edit(/lockflight.yaml)");

        DoctorCheck check = await CheckAsync("deny rules");

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Contains("1 protected paths have no Edit deny rule: Edit(/lockflight.yaml)", check.Detail, StringComparison.Ordinal);
        Assert.Contains("init", check.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASandboxWriteDenialRemovedAfterInitIsNamedWithTheFix()
    {
        await InitAsync();
        RemoveFromSettings("sandbox", "filesystem", "denyWrite", "./lockflight.yaml");

        DoctorCheck check = await CheckAsync("sandbox");

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Contains("./lockflight.yaml", check.Detail, StringComparison.Ordinal);
        Assert.Contains("init", check.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheCiOnlyLevelReportsNoMissingPluginHooksOrEngineAsAProblem()
    {
        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["init", "--ci-only"], string.Empty, root.FullName);
        Assert.True(exitCode == ExitCode.Pass, error);

        IReadOnlyList<DoctorCheck> checks = await ReadinessChecks.RunAsync(root.FullName, userHome: null, nugetPackages: null, gitleaks: null, EnvironmentReader.Of(new Dictionary<string, string>()), TimeProvider.System, TestContext.Current.CancellationToken);

        Assert.Equal(CheckStatus.Info, Assert.Single(checks, check => check.Name == "linked engine").Status);
        DoctorCheck settings = Assert.Single(checks, check => check.Name == "claude settings");
        Assert.Equal(CheckStatus.Info, settings.Status);
        Assert.StartsWith("CI-only level", settings.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(checks, check => check.Name is "deny rules" or "sandbox" or "claude code");
    }

    [Fact]
    public async Task SettingsWithLockflightDenyRulesAreNotTheCiOnlyLevel()
    {
        Write(".claude/settings.json", """{ "permissions": { "deny": ["Edit(/lockflight.yaml)"] } }""");

        DoctorCheck check = await CheckAsync("claude settings");

        Assert.Equal(CheckStatus.Warn, check.Status);
    }

    [Fact]
    public async Task SettingsWithoutEditDenyRulesWarnWithTheFix()
    {
        Write(".claude/settings.json", """{ "enabledPlugins": { "lockflight@lockflight": true } }""");

        DoctorCheck check = await CheckAsync("deny rules");

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Contains("lists no Edit deny rules", check.Detail, StringComparison.Ordinal);
        Assert.Contains("init", check.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SettingsWithoutSandboxWriteDenialsWarnWithTheFix()
    {
        Write(".claude/settings.json", """{ "enabledPlugins": { "lockflight@lockflight": true } }""");

        DoctorCheck check = await CheckAsync("sandbox");

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Contains("init", check.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProtectedFileMissingFromTheSandboxWriteDenialsIsNamed()
    {
        Write("global.json", "{}");
        Write(".claude/settings.json", """{ "sandbox": { "filesystem": { "denyWrite": ["./.lockflight/bin"] } } }""");

        DoctorCheck check = await CheckAsync("sandbox");

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Contains("./global.json", check.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteSandboxWriteDenialsSayWhetherTheSandboxIsOn()
    {
        Write(".claude/settings.json", """{ "sandbox": { "filesystem": { "denyWrite": ["./.lockflight/bin", "./.claude/settings.json", "./.claude/settings.local.json"] } } }""");
        DoctorCheck off = await CheckAsync("sandbox");
        Write(".claude/settings.local.json", """{ "sandbox": { "enabled": true } }""");

        DoctorCheck on = await CheckAsync("sandbox");

        Assert.Equal(CheckStatus.Pass, off.Status);
        Assert.Equal(CheckStatus.Pass, on.Status);
        if (!OperatingSystem.IsWindows())
        {
            Assert.StartsWith("off:", off.Detail, StringComparison.Ordinal);
            Assert.StartsWith("on:", on.Detail, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AToolManifestOfTheWrongShapeFailsWithItsFix()
    {
        Write("dotnet-tools.json", """{ "version": 1, "tools": [] }""");

        DoctorCheck check = await CheckAsync("tool pin");

        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Contains("dotnet-tools.json", check.Detail, StringComparison.Ordinal);
        Assert.NotNull(check.Fix);
    }

    [Fact]
    public void ADevelopmentPinOnNoLocalFeedAndNotInTheToolsCacheFails()
    {
        PinEngine(DevelopmentVersion);
        string home = HomeWithUserInstall();

        DoctorCheck check = ReadinessChecks.CheckPinRestorable(root.FullName, home, nugetPackages: null)!;

        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Contains(DevelopmentVersion, check.Detail, StringComparison.Ordinal);
        Assert.Contains("~/.lockflight/bin/lockflight update", check.Fix, StringComparison.Ordinal);
        Assert.Contains("separate terminal window", check.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutAnInstallFromSourceTheFixIsToInstallOne()
    {
        PinEngine(DevelopmentVersion);

        DoctorCheck check = ReadinessChecks.CheckPinRestorable(root.FullName, Path.Combine(root.FullName, "home"), nugetPackages: null)!;

        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Contains("scripts/dev-install.sh", check.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void ADevelopmentPinOnlyInTheLocalFeedWarnsThatAPlainRestoreCannotGetIt()
    {
        PinEngine(DevelopmentVersion);
        string home = HomeWithUserInstall();
        Write($"home/.lockflight/feed/Lockflight.Cli.{DevelopmentVersion}.nupkg", string.Empty);

        DoctorCheck check = ReadinessChecks.CheckPinRestorable(root.FullName, home, nugetPackages: null)!;

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Contains("--add-source", check.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void ADevelopmentPinInTheToolsCachePasses()
    {
        PinEngine(DevelopmentVersion);
        Directory.CreateDirectory(Path.Combine(root.FullName, "packages", "lockflight.cli", DevelopmentVersion));

        DoctorCheck check = ReadinessChecks.CheckPinRestorable(root.FullName, Path.Combine(root.FullName, "home"), Path.Combine(root.FullName, "packages"))!;

        Assert.Equal(CheckStatus.Pass, check.Status);
    }

    [Fact]
    public void TheToolsCacheDefaultsToTheNuGetFolderInTheHome()
    {
        PinEngine(DevelopmentVersion);
        Directory.CreateDirectory(Path.Combine(root.FullName, "home", ".nuget", "packages", "lockflight.cli", DevelopmentVersion));

        DoctorCheck check = ReadinessChecks.CheckPinRestorable(root.FullName, Path.Combine(root.FullName, "home"), nugetPackages: null)!;

        Assert.Equal(CheckStatus.Pass, check.Status);
    }

    [Fact]
    public void AReleasePinIsNotLookedUpOnAnyFeed()
    {
        PinEngine("0.2.0-alpha.2");

        Assert.Null(ReadinessChecks.CheckPinRestorable(root.FullName, Path.Combine(root.FullName, "home"), nugetPackages: null));
    }

    [Theory]
    [InlineData("0.2.0-alpha.4.dev.20261015101143", "0.2.0-alpha.4")]
    [InlineData("0.2.0-alpha.4.dev.20261015101143", "0.2.0-alpha.5")]
    [InlineData(DevelopmentVersion, "0.2.0-alpha.4")]
    public void ADevelopmentPinWithAReleaseInstalledIsMovedToTheRelease(string pinned, string installed)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        PinEngine(pinned);
        string home = HomeWithEngineLinkedTo(installed);

        DoctorCheck check = ReadinessChecks.CheckDevelopmentPin(root.FullName, home, nugetPackages: null)!;

        Assert.Equal("development pin", check.Name);
        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Contains($"release {installed} is installed", check.Detail, StringComparison.Ordinal);
        Assert.Contains("~/.lockflight/bin/lockflight update", check.Fix, StringComparison.Ordinal);
        Assert.Contains("separate terminal window", check.Fix, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0.2.0-alpha.3")]
    [InlineData("0.2.0-alpha.5.dev.20261016000000")]
    public void ADevelopmentPinWithNoReleaseAtItsBaseInstalledIsCheckedForRestoreOnly(string installed)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        PinEngine("0.2.0-alpha.4.dev.20261015101143");
        string home = HomeWithEngineLinkedTo(installed);

        DoctorCheck check = ReadinessChecks.CheckDevelopmentPin(root.FullName, home, nugetPackages: null)!;

        Assert.Equal("pin restorable", check.Name);
    }

    [Fact]
    public void WithoutAPinThereIsNoRestoreCheck() =>
        Assert.Null(ReadinessChecks.CheckPinRestorable(root.FullName, Path.Combine(root.FullName, "home"), nugetPackages: null));

    public void Dispose() => root.Delete(recursive: true);

    [Fact]
    public async Task ALinkedEngineReplacedSinceItWasLinkedFailsWithoutBeingRun()
    {
        string marker = Path.Combine(root.FullName, "ran");
        WriteEngine($"#!/bin/sh\ntouch '{marker}'\necho {ToolPin.RunningVersion}\n");
        Write($"{LinkCommand.RelativeBinDirectory}/{EngineRecord.FileName}", new string('0', 64) + "\n");

        DoctorCheck link = await CheckAsync("linked engine");

        Assert.Equal(CheckStatus.Fail, link.Status);
        Assert.Contains("replaced since it was linked", link.Detail, StringComparison.Ordinal);
        Assert.False(File.Exists(marker));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ALinkedEngineWithoutARecordedHashFailsWithoutBeingRunAndNamesUpdate(string? record)
    {
        string marker = Path.Combine(root.FullName, "ran");
        WriteEngine($"#!/bin/sh\ntouch '{marker}'\necho {ToolPin.RunningVersion}\n");
        if (record is not null)
        {
            Write($"{LinkCommand.RelativeBinDirectory}/{EngineRecord.FileName}", record);
        }

        DoctorCheck link = await CheckAsync("linked engine");

        Assert.Equal(CheckStatus.Fail, link.Status);
        Assert.Contains("no hash recorded", link.Detail, StringComparison.Ordinal);
        Assert.Contains($"a person opens a separate terminal window ({HumanOnlyCommands.NotWithBang}) and runs `~/.lockflight/bin/lockflight update`", link.Fix, StringComparison.Ordinal);
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task ALinkedEngineThatMatchesItsRecordPasses()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The stand-in engine is a POSIX sh script.");
        string binary = WriteEngine($"#!/bin/sh\necho {ToolPin.RunningVersion}\n");
        Write($"{LinkCommand.RelativeBinDirectory}/{EngineRecord.FileName}", EngineRecord.HashOf(binary) + "\n");

        DoctorCheck link = await CheckAsync("linked engine");

        Assert.Equal(CheckStatus.Pass, link.Status);
    }

    private string WriteEngine(string script)
    {
        string binary = LinkCommand.BinaryPath(root.FullName);
        Directory.CreateDirectory(Path.GetDirectoryName(binary)!);
        File.WriteAllText(binary, script);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(binary, LinkCommand.ExecutableMode);
        }

        return binary;
    }

    private async Task<DoctorCheck> CheckAsync(string name)
    {
        IReadOnlyList<DoctorCheck> checks = await ReadinessChecks.RunAsync(root.FullName, userHome: null, nugetPackages: null, gitleaks: null, EnvironmentReader.Of(new Dictionary<string, string>()), TimeProvider.System, TestContext.Current.CancellationToken);
        return Assert.Single(checks, check => check.Name == name);
    }

    private async Task InitAsync()
    {
        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["init", "--no-link"], string.Empty, root.FullName);
        Assert.True(exitCode == ExitCode.Pass, error);
    }

    /// <summary>Removes one entry from the list at <paramref name="path"/> in the project's Claude Code settings, as a teammate might.</summary>
    private void RemoveFromSettings(params string[] path)
    {
        string settingsPath = Path.Combine(root.FullName, ClaudeSettingsMerger.RelativePath);
        JsonNode settings = JsonNode.Parse(File.ReadAllText(settingsPath))!;
        JsonArray list = path[..^1].Aggregate(settings, (node, key) => node[key]!).AsArray();
        Assert.True(list.Remove(list.Single(entry => entry!.GetValue<string>() == path[^1])));
        File.WriteAllText(settingsPath, settings.ToJsonString());
    }

    private void Write(string relativePath, string content)
    {
        string path = Path.Combine(root.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private void PinEngine(string version) =>
        Write("dotnet-tools.json", $$"""{ "version": 1, "isRoot": true, "tools": { "lockflight.cli": { "version": "{{version}}", "commands": ["lockflight"] } } }""");

    private string HomeWithEngineLinkedTo(string version)
    {
        string home = Path.Combine(root.FullName, "home");
        Write($"home/.lockflight/tools/{version}/lockflight", string.Empty);
        Directory.CreateDirectory(Path.Combine(home, ".lockflight", "bin"));
        File.CreateSymbolicLink(Path.Combine(home, ".lockflight", "bin", "lockflight"), Path.Combine(home, ".lockflight", "tools", version, "lockflight"));
        return home;
    }

    private string HomeWithUserInstall()
    {
        Write("home/.lockflight/bin/lockflight", string.Empty);
        return Path.Combine(root.FullName, "home");
    }
}
