using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Adoption;

/// <summary>A small existing setup: a guard hook, a team skill, Spec Kit, two features, a child repository.</summary>
public sealed class AdoptCommandTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-adopt-");

    [Fact]
    public async Task PlanApplyAndUndoRoundTripByteForByte()
    {
        await CreateSetupAsync();
        Dictionary<string, string> before = Snapshot();

        (ExitCode plan, string planOutput, _) = await TestConsole.RunAsync(["adopt", "plan"], string.Empty, root.FullName);
        (ExitCode apply, string applyOutput, string applyError) = await TestConsole.RunAsync(["adopt", "apply", "--skip-speckit"], string.Empty, root.FullName);
        Manifest adopted = ManifestLocator.Discover(root.FullName).Manifest!;
        string claudeMd = File.ReadAllText(Path.Combine(root.FullName, "CLAUDE.md"));
        (ExitCode undo, _, _) = await TestConsole.RunAsync(["adopt", "undo"], string.Empty, root.FullName);

        Assert.Equal(ExitCode.Pass, plan);
        Assert.Contains("protect", planOutput, StringComparison.Ordinal);
        Assert.True(apply == ExitCode.Pass, applyError);
        Assert.Contains("1 paths added to protected:", applyOutput, StringComparison.Ordinal);
        Assert.Contains("**/appsettings.Local.json", adopted.ProtectedGlobs);
        Assert.Equal(["services/orders"], adopted.Units.Select(unit => unit.Path));
        Assert.Contains("<!-- lockflight:begin -->", claudeMd, StringComparison.Ordinal);
        Assert.Contains("2 existing features marked", applyOutput, StringComparison.Ordinal);
        Assert.Equal(ExitCode.Pass, undo);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task ThePlanGivesTheFullApplyCommandWithThePreset()
    {
        await CreateSetupAsync();
        DirectoryInfo home = Directory.CreateTempSubdirectory("lockflight-home-");
        try
        {
            string checkout = Path.Combine(home.FullName, "lockflight");
            string preset = Path.Combine(checkout, "plugins", "lockflight", "speckit-preset");
            Directory.CreateDirectory(preset);
            Directory.CreateDirectory(Path.Combine(home.FullName, ".lockflight"));
            File.WriteAllText(Path.Combine(home.FullName, ".lockflight", "source"), checkout);

            (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["adopt", "plan"], string.Empty, root.FullName, new Dictionary<string, string> { ["HOME"] = home.FullName });

            string markdown = File.ReadAllText(Path.Combine(root.FullName, ".lockflight", "adopt", "plan.md"));
            Assert.Equal(ExitCode.Pass, exitCode);
            Assert.Contains($"adopt apply --speckit-preset '{preset}'", markdown, StringComparison.Ordinal);
            Assert.Contains("--skip-speckit", markdown, StringComparison.Ordinal);
            Assert.Contains($"adopt apply --speckit-preset '{preset}'", output, StringComparison.Ordinal);
        }
        finally
        {
            home.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ApplyAndUndoAreRefusedInsideAClaudeCodeSession()
    {
        await CreateSetupAsync();
        Dictionary<string, string> agent = new() { [EnvironmentVariables.ClaudeCode] = "1" };

        (ExitCode apply, _, string error) = await TestConsole.RunAsync(["adopt", "apply"], string.Empty, root.FullName, agent);
        (ExitCode undo, _, _) = await TestConsole.RunAsync(["adopt", "undo"], string.Empty, root.FullName, agent);
        (ExitCode plan, _, _) = await TestConsole.RunAsync(["adopt", "plan"], string.Empty, root.FullName, agent);

        Assert.Equal(ExitCode.HookBlock, apply);
        Assert.Equal(ExitCode.HookBlock, undo);
        Assert.Contains("human decision", error, StringComparison.Ordinal);
        Assert.Equal(ExitCode.Pass, plan);
    }

    [Fact]
    public async Task ApplyRefusesWithoutAReviewedPlanOrWithADirtyTree()
    {
        await CreateSetupAsync();
        (ExitCode withoutPlan, _, string noPlanError) = await TestConsole.RunAsync(["adopt", "apply"], string.Empty, root.FullName);
        await TestConsole.RunAsync(["adopt", "plan"], string.Empty, root.FullName);
        File.WriteAllText(Path.Combine(root.FullName, "CLAUDE.md"), "# Team\nedited\n");
        (ExitCode dirty, _, string dirtyError) = await TestConsole.RunAsync(["adopt", "apply"], string.Empty, root.FullName);

        Assert.Equal(ExitCode.Config, withoutPlan);
        Assert.Contains("adopt plan", noPlanError, StringComparison.Ordinal);
        Assert.Equal(ExitCode.Config, dirty);
        Assert.Contains("clean working tree", dirtyError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UndoSkipsABackupOfAnApplyThatDidNotFinish()
    {
        await CreateSetupAsync();
        Dictionary<string, string> before = Snapshot();
        await TestConsole.RunAsync(["adopt", "plan"], string.Empty, root.FullName);
        await TestConsole.RunAsync(["adopt", "apply", "--skip-speckit"], string.Empty, root.FullName);
        string backups = Path.Combine(root.FullName, ".lockflight", "backup");
        File.Copy(Directory.GetFiles(backups, "*.tar.gz").Single(), Path.Combine(backups, "99991231235959999.tar.gz"));

        (ExitCode unfinished, _, string error) = await TestConsole.RunAsync(["adopt", "undo", "--backup", "99991231235959999"], string.Empty, root.FullName);
        (ExitCode undo, string output, _) = await TestConsole.RunAsync(["adopt", "undo"], string.Empty, root.FullName);

        Assert.Equal(ExitCode.Config, unfinished);
        Assert.Contains("did not finish", error, StringComparison.Ordinal);
        Assert.Equal(ExitCode.Pass, undo);
        Assert.DoesNotContain("99991231235959999", output, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot());
    }

    private async Task CreateSetupAsync()
    {
        Write(Manifest.FileName, "version: 1\nprotected: []\n");
        Write(".gitignore", ".lockflight/\nservices/\n");
        Write("CLAUDE.md", "# Team\n");
        Write(".claude/settings.json", """{ "hooks": { "PreToolUse": [ { "matcher": "Edit|Write", "hooks": [ { "type": "command", "command": "$CLAUDE_PROJECT_DIR/.claude/hooks/guard.sh" } ] } ] } }""");
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

    /// <summary>Every file outside .git and .lockflight, with its content.</summary>
    private Dictionary<string, string> Snapshot() => Directory.EnumerateFiles(root.FullName, "*", SearchOption.AllDirectories)
        .Select(path => Path.GetRelativePath(root.FullName, path).Replace('\\', '/'))
        .Where(path => !path.StartsWith(".git/", StringComparison.Ordinal) && !path.StartsWith(".lockflight/", StringComparison.Ordinal) && !path.Contains("/.git/", StringComparison.Ordinal))
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
