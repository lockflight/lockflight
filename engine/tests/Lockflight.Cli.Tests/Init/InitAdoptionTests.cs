using Lockflight.Cli.Config;
using Lockflight.Cli.Gates.Baselines;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Init;

/// <summary>Init on a small existing setup: a guard hook, a team skill, Spec Kit, two features, a child repository.</summary>
public sealed class InitAdoptionTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-init-adopt-");

    [Fact]
    public async Task InitAdoptsAnExistingSetupAndKeepsTheTeamsFiles()
    {
        await CreateSetupAsync();

        (ExitCode exitCode, string output, string error) = await TestConsole.RunAsync(["init", "--no-link"], string.Empty, root.FullName);

        Assert.True(exitCode == ExitCode.Pass, error);
        Manifest manifest = ManifestLocator.Discover(root.FullName).Manifest!;
        Assert.Contains("**/appsettings.Local.json", manifest.ProtectedGlobs);
        Assert.Equal(["services/orders"], manifest.Units.Select(unit => unit.Path));
        string claudeMd = File.ReadAllText(Path.Combine(root.FullName, "CLAUDE.md"));
        Assert.StartsWith("# Team\n", claudeMd, StringComparison.Ordinal);
        Assert.Contains("<!-- lockflight:begin -->", claudeMd, StringComparison.Ordinal);
        Assert.Equal(["001-a", "001-b"], new AdoptedFeatures(root.FullName).Load().Order(StringComparer.Ordinal));
        string settings = File.ReadAllText(Path.Combine(root.FullName, ".claude", "settings.json"));
        Assert.Contains("guard.sh", settings, StringComparison.Ordinal);
        Assert.Contains("Edit(/**/appsettings.Local.json)", settings, StringComparison.Ordinal);
        Assert.Equal("---\nname: team\n---\n", File.ReadAllText(Path.Combine(root.FullName, ".claude", "skills", "team", "SKILL.md")));
        Assert.Contains("--speckit-preset", output, StringComparison.Ordinal);
        Assert.Contains("commit them as one commit", output, StringComparison.Ordinal);
        Assert.Contains("git revert", output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(root.FullName, ".lockflight", "backup")));
    }

    [Fact]
    public async Task ADryRunPrintsThePlanAndWritesNothing()
    {
        await CreateSetupAsync();
        Dictionary<string, string> before = Snapshot();

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["init", "--dry-run"], string.Empty, root.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Equal(before, Snapshot());
        Assert.False(Directory.Exists(Path.Combine(root.FullName, ".lockflight")));
        Assert.Contains("nothing was written", output, StringComparison.Ordinal);
        Assert.Contains($"would create     {Manifest.FileName}", output, StringComparison.Ordinal);
        Assert.Contains("would update     CLAUDE.md", output, StringComparison.Ordinal);
        Assert.Contains("# Lockflight adoption plan", output, StringComparison.Ordinal);
        Assert.Contains("| protect |", output, StringComparison.Ordinal);
        Assert.Contains("marks 2 existing features as predating Lockflight: 001-a, 001-b", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADryRunAfterInitShowsNothingLeftToDo()
    {
        await CreateSetupAsync();
        await TestConsole.RunAsync(["init", "--no-link"], string.Empty, root.FullName);

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["init", "--dry-run"], string.Empty, root.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.DoesNotContain("would create", output, StringComparison.Ordinal);
        Assert.DoesNotContain("would update", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnlyTheInitThatCreatesTheManifestMarksFeatures()
    {
        await CreateSetupAsync();
        Write(Manifest.FileName, "version: 1\n");

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["init", "--no-link"], string.Empty, root.FullName);

        Assert.True(exitCode == ExitCode.Pass, error);
        Assert.Empty(new AdoptedFeatures(root.FullName).Load());
        Assert.Contains("**/appsettings.Local.json", ManifestLocator.Discover(root.FullName).Manifest!.ProtectedGlobs);
    }

    [Fact]
    public async Task AnExistingManifestGetsItsUnitsAsLinesForAPersonNotAnEdit()
    {
        await CreateSetupAsync();
        Write(Manifest.FileName, "version: 1\n");

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["init", "--no-link"], string.Empty, root.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Empty(ManifestLocator.Discover(root.FullName).Manifest!.Units);
        Assert.Contains("manual     add under units: in lockflight.yaml: path: services/orders", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AVendoredUnitRepositoryGetsNoFeatureMarked()
    {
        await CreateSetupAsync();
        Write(Manifest.UnitFileName, "version: 1\n");

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["init", "--no-link"], string.Empty, root.FullName);

        Assert.True(exitCode == ExitCode.Pass, error);
        Assert.Empty(new AdoptedFeatures(root.FullName).Load());
    }

    [Fact]
    public async Task ExistingLockflightStateWithoutAManifestGetsNoFeatureMarked()
    {
        await CreateSetupAsync();
        Write(".lockflight/ledger/events-2026-10.jsonl", "{}\n");

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["init", "--no-link"], string.Empty, root.FullName);

        Assert.True(exitCode == ExitCode.Pass, error);
        Assert.Empty(new AdoptedFeatures(root.FullName).Load());
    }

    [Fact]
    public async Task TheSpecKitUpgradeIsRefusedInsideAClaudeCodeSession()
    {
        await CreateSetupAsync();
        Dictionary<string, string> before = Snapshot();
        string preset = Directory.CreateDirectory(Path.Combine(root.FullName, "preset")).FullName;

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(
            ["init", "--no-link", "--speckit-preset", preset], string.Empty, root.FullName, new Dictionary<string, string> { [EnvironmentVariables.ClaudeCode] = "1" });

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("a person's decision", error, StringComparison.Ordinal);
        Assert.Contains("separate terminal window", error, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task UnitOfWritesTheShellPointerOnce()
    {
        await GitAsync(root.FullName, ["init", "-q"]);

        (ExitCode first, _, string error) = await TestConsole.RunAsync(
            ["init", "--no-link", "--unit-of", "git@example.com:example/shell.git", "--unit-path", "services/orders"], string.Empty, root.FullName);
        string manifestText = File.ReadAllText(Path.Combine(root.FullName, Manifest.FileName));
        (ExitCode second, string again, _) = await TestConsole.RunAsync(
            ["init", "--no-link", "--unit-of", "git@example.com:example/shell.git", "--unit-path", "services/orders"], string.Empty, root.FullName);

        Assert.True(first == ExitCode.Pass, error);
        ShellSettings shell = ManifestLocator.Discover(root.FullName).Manifest!.Shell!;
        Assert.Equal("git@example.com:example/shell.git", shell.Url);
        Assert.Equal("services/orders", shell.Unit);
        Assert.Null(shell.Ref);
        Assert.Equal(ExitCode.Pass, second);
        Assert.Equal(manifestText, File.ReadAllText(Path.Combine(root.FullName, Manifest.FileName)));
        Assert.Contains($"unchanged  {Manifest.FileName}", again, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://user:token@example.com/example/shell.git", "credentials")]
    [InlineData("https://example.com/example/shell.git?token=x", "credentials")]
    [InlineData("ftp://example.com/shell.git", "https:// or SSH URL")]
    public async Task UnitOfRefusesAUrlWithCredentialsOrAnUnknownScheme(string url, string expected)
    {
        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["init", "--no-link", "--unit-of", url, "--unit-path", "services/orders"], string.Empty, root.FullName);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains(expected, error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root.FullName, Manifest.FileName)));
    }

    [Fact]
    public async Task UnitOfIsRefusedInARepositoryWithUnits()
    {
        Write(Manifest.FileName, "version: 2\nunits:\n  - path: services/orders\n");

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["init", "--no-link", "--unit-of", "https://example.com/example/shell.git", "--unit-path", "services/orders"], string.Empty, root.FullName);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("lists units:", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a #b")]
    [InlineData("../orders")]
    [InlineData("\"orders\"")]
    public async Task AUnitPathThatNeedsQuotingIsRefused(string unitPath)
    {
        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(
            ["init", "--no-link", "--unit-of", "https://example.com/shell.git", "--unit-path", unitPath], string.Empty, root.FullName);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("--unit-path takes", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnitOfOnAVersionOneManifestSaysToMoveToVersionTwo()
    {
        Write(Manifest.FileName, "version: 1\n");

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(
            ["init", "--no-link", "--unit-of", "https://example.com/example/shell.git", "--unit-path", "services/orders"], string.Empty, root.FullName);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("needs a version 2 manifest", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnitPathWithoutUnitOfIsAUsageError()
    {
        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["init", "--no-link", "--unit-path", "services/orders"], string.Empty, root.FullName);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("go together", error, StringComparison.Ordinal);
    }

    private async Task CreateSetupAsync()
    {
        Write(".gitignore", ".lockflight/\nservices/\n");
        Write("CLAUDE.md", "# Team\n");
        Write(".claude/settings.json", """{ "hooks": { "PreToolUse": [ { "matcher": "Edit|Write", "hooks": [ { "type": "command", "command": "\"$CLAUDE_PROJECT_DIR\"/.claude/hooks/guard.sh" } ] } ] } }""");
        Write(".claude/hooks/guard.sh", "#!/bin/sh\ncase \"$(cat)\" in\n  *appsettings.Local.json*) exit 2 ;;\nesac\nexit 0\n");
        Write(".claude/skills/team/SKILL.md", "---\nname: team\n---\n");
        Write(".specify/init-options.json", """{ "speckit_version": "1.1.0" }""");
        Write("specs/001-a/spec.md", "spec a\n");
        Write("specs/001-b/spec.md", "spec b\n");
        Write("services/orders/Orders.sln", string.Empty);
        await GitAsync(root.FullName, ["init", "-q"]);
        await GitAsync(root.FullName, ["add", "."]);
        await GitAsync(root.FullName, ["-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qm", "setup"]);
        await GitAsync(Path.Combine(root.FullName, "services", "orders"), ["init", "-q"]);
    }

    /// <summary>Every file outside .git, with its content.</summary>
    private Dictionary<string, string> Snapshot() => Directory.EnumerateFiles(root.FullName, "*", SearchOption.AllDirectories)
        .Select(path => Path.GetRelativePath(root.FullName, path).Replace('\\', '/'))
        .Where(path => !path.StartsWith(".git/", StringComparison.Ordinal) && !path.Contains("/.git/", StringComparison.Ordinal))
        .ToDictionary(path => path, path => File.ReadAllText(Path.Combine(root.FullName, path)), StringComparer.Ordinal);

    private void Write(string relative, string text)
    {
        string path = Path.Combine(root.FullName, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static async Task GitAsync(string directory, string[] arguments)
    {
        ProcessResult result = await ProcessRunner.RunAsync("git", arguments, directory, TestContext.Current.CancellationToken);
        Assert.True(result.ExitCode == 0, result.Combined);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);
}
