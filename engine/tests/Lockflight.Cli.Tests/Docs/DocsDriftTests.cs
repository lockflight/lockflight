using Lockflight.Cli.Docs;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Docs;

public sealed class DocsDriftTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-drift-");

    [Fact]
    public async Task ALinkToAMissingFileIsReported()
    {
        Write("docs/guide.md", "See [setup](setup.md) and [home](../README.md).\n");
        Write("README.md", "# Home\n");
        await CommitAsync("docs");

        DriftFinding finding = Assert.Single(await FindAsync());

        Assert.Equal(("docs/guide.md", 1, "setup.md", DriftKind.Link), (finding.File, finding.Line, finding.Reference, finding.Kind));
    }

    [Fact]
    public async Task LinksToUrlsAnchorsInsideTheFileAndExistingDirectoriesResolve()
    {
        Write("docs/guide.md", "# Guide\n\n## First step\n\n[web](https://example.com) [mail](mailto:a@b.c) [up](#first-step) [src](../src/) [root](/src/App.cs)\n");
        Write("src/App.cs", "class App { }\n");
        await CommitAsync("docs");

        Assert.Empty(await FindAsync());
    }

    [Fact]
    public async Task AnAnchorWithNoMatchingHeadingIsReported()
    {
        Write("docs/a.md", "[b](b.md#the-second-part) [ok](b.md#part-one) [dup](b.md#part-one-1)\n");
        Write("docs/b.md", "# B\n\n## Part `one`\n\n## Part one\n");
        await CommitAsync("docs");

        DriftFinding finding = Assert.Single(await FindAsync());

        Assert.Equal((DriftKind.Anchor, "b.md#the-second-part"), (finding.Kind, finding.Reference));
    }

    [Fact]
    public async Task ACodeSpanPathThatTheRepositoryNeverHadIsNotReported()
    {
        Write("CLAUDE.md", "Run `.lockflight/bin/lockflight doctor`; the user's `src/Api/Startup.cs` is an example; see `docs/later.md`.\n");
        await CommitAsync("instructions");

        Assert.Empty(await FindAsync());
    }

    [Fact]
    public async Task ACodeSpanPathDeletedInHistoryIsReportedWithTheCommit()
    {
        Write("scripts/build.sh", "echo build\n");
        await CommitAsync("add the build script");
        File.Delete(Path.Combine(root.FullName, "scripts/build.sh"));
        Write("scripts/other.sh", "echo other\n");
        Write("CLAUDE.md", "Build with `scripts/build.sh`.\n");
        await CommitAsync("remove the build script");

        DriftFinding finding = Assert.Single(await FindAsync());

        Assert.Equal((DriftKind.Path, "scripts/build.sh"), (finding.Kind, finding.Reference));
        Assert.StartsWith("deleted in ", finding.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARenamedPathNamesTheNewPath()
    {
        Write("src/Gates/BuildStep.cs", "class BuildStep { public int Run() => 1; }\n");
        await CommitAsync("add");
        await GitAsync(["mv", "src/Gates/BuildStep.cs", "src/Gates/CompileStep.cs"]);
        Write("docs/notes.md", "The build is in `src/Gates/BuildStep.cs:12`.\n");
        await CommitAsync("rename");

        DriftFinding finding = Assert.Single(await FindAsync());

        Assert.Contains("renamed to src/Gates/CompileStep.cs", finding.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APathWhoseFileLivesInAnotherDirectoryIsANearMiss()
    {
        Write("src/Gates/Steps/BuildStep.cs", "class BuildStep { }\n");
        Write("src/Gates/GateRunner.cs", "class GateRunner { }\n");
        Write("CLAUDE.md", "The build step is `src/Gates/BuildStep.cs`.\n");
        await CommitAsync("docs");

        DriftFinding finding = Assert.Single(await FindAsync());

        Assert.Contains("src/Gates/Steps/BuildStep.cs", finding.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUntrackedFileTheAgentJustWroteResolves()
    {
        Write("CLAUDE.md", "Read [the plan](docs/plan.md).\n");
        await CommitAsync("docs");
        Write("docs/plan.md", "# Plan\n");

        Assert.Empty(await FindAsync());
    }

    [Fact]
    public async Task AnUnknownEngineOptionAndCommandAreReported()
    {
        Write("docs/run.md", """
            Run `lockflight gate run --profil pr` or `.lockflight/bin/lockflight gate check`.

            ```bash
            $ dotnet lockflight doctor --canaries   # fine
            cd x && .lockflight/bin/lockflight review status --since main
            ```

            ```
            lockflight engine (pinned) ───┐
            ```
            """);
        await CommitAsync("docs");

        IReadOnlyList<DriftFinding> findings = await FindAsync();

        Assert.Equal(["--profil", "gate check", "--since"], findings.Select(Detail));
        Assert.All(findings, finding => Assert.Equal(DriftKind.Command, finding.Kind));
    }

    [Fact]
    public async Task PlaceholdersAlternativesAndUsageBracketsAreNotDrift()
    {
        Write("docs/run.md", """
            `lockflight approve spec|tasks [--sign]` `lockflight propose <path> --from <file>` `lockflight approve`
            `lockflight <command> --help` `lockflight baseline <build|tests|deps>` `lockflight doctor --canaries[=full]`
            `lockflight gate run --profile=pr` `lockflight redgreen amend <test> --reason "<why>"`
            """);
        await CommitAsync("docs");

        Assert.Empty(await FindAsync());
    }

    [Fact]
    public async Task ASlashCommandOfATrackedPluginMustExist()
    {
        Write("plugins/acme/skills/approve/SKILL.md", "---\nname: approve\n---\n");
        Write("docs/use.md", "Type `/acme:approve spec`, not `/acme:aprove`; `/other:thing` belongs to an installed plugin.\n");
        await CommitAsync("docs");

        DriftFinding finding = Assert.Single(await FindAsync());

        Assert.Equal((DriftKind.Command, "/acme:aprove"), (finding.Kind, finding.Reference));
    }

    [Fact]
    public async Task HistoryDocumentsSpecKitSkillsAndIgnoredLinesAreNotScanned()
    {
        Write("CHANGELOG.md", "[gone](gone.md)\n");
        Write("docs/decisions/0001.md", "[gone](gone.md)\n");
        Write(".claude/skills/speckit-plan/SKILL.md", "[gone](gone.md)\n");
        Write("docs/guide.md", "[gone](gone.md) <!-- lockflight: drift-ignore -->\n");
        Write("src/notes.md", "[gone](gone.md)\n");
        await CommitAsync("docs");

        Assert.Empty(await FindAsync());
    }

    [Fact]
    public async Task ALongClaudeMdGetsABudgetFindingNamingItsLongestSections()
    {
        string steps = string.Concat(Enumerable.Range(1, 150).Select(index => $"{index}. step {index}\n"));
        string rules = string.Concat(Enumerable.Range(1, 60).Select(index => $"- rule {index}\n"));
        Write("CLAUDE.md", $"# Project\n\n## Release\n\n{steps}\n## Style\n\n{rules}");
        await CommitAsync("docs");

        DriftFinding finding = Assert.Single(await FindAsync());

        Assert.Equal(DriftKind.Budget, finding.Kind);
        Assert.Contains("under 200", finding.Reason, StringComparison.Ordinal);
        Assert.Contains("'Release' (153 lines): a skill", finding.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OutsideAGitRepositoryNothingIsScanned()
    {
        Write("CLAUDE.md", "[gone](gone.md)\n");

        DocsDriftReport report = await DocsDrift.FindAsync(root.FullName, TestContext.Current.CancellationToken);

        Assert.False(report.Scanned);
    }

    [Fact]
    public async Task TheCommandListsFindingsAndExitsOneOrSaysEverythingResolves()
    {
        Write("CLAUDE.md", "See [setup](setup.md).\n");
        await CommitAsync("docs");

        (ExitCode failed, string listing, _) = await TestConsole.RunAsync(["docs", "check"], string.Empty, root.FullName);
        Write("setup.md", "# Setup\n");
        (ExitCode passed, string clean, _) = await TestConsole.RunAsync(["docs", "check"], string.Empty, root.FullName);

        Assert.Equal((ExitCode.Fail, ExitCode.Pass), (failed, passed));
        Assert.Contains("CLAUDE.md:1  setup.md  — no such file", listing, StringComparison.Ordinal);
        Assert.Contains("every reference resolves", clean, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheCommandOutsideAGitRepositoryIsAConfigurationError()
    {
        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["docs", "check"], string.Empty, root.FullName);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("not a git repository", error, StringComparison.Ordinal);
    }

    private static string Detail(DriftFinding finding) => finding.Reason.Split('\'') is { Length: >= 2 } parts ? parts[^2] : finding.Reason;

    private async Task<IReadOnlyList<DriftFinding>> FindAsync() =>
        (await DocsDrift.FindAsync(root.FullName, TestContext.Current.CancellationToken)).Findings;

    private void Write(string relativePath, string content)
    {
        string path = Path.Combine(root.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private async Task CommitAsync(string message)
    {
        if (!Directory.Exists(Path.Combine(root.FullName, ".git")))
        {
            await GitAsync(["init", "--quiet", "--initial-branch=main"]);
        }

        await GitAsync(["add", "--all"]);
        await GitAsync(["-c", "user.name=Test", "-c", "user.email=test@example.com", "commit", "--quiet", "--allow-empty", "-m", message]);
    }

    private async Task GitAsync(string[] arguments)
    {
        ProcessResult result = await ProcessRunner.RunAsync("git", arguments, root.FullName, TestContext.Current.CancellationToken);
        Assert.True(result.ExitCode == 0, result.Combined);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);
}
