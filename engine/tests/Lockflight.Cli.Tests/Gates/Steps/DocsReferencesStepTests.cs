using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Gates.Steps;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Gates.Steps;

/// <summary>The references step over a scratch repository: what the working tree breaks, compared with the last commit or a merge base.</summary>
public sealed class DocsReferencesStepTests : IDisposable
{
    private const string Claude = "# Project\n\nRead [the guide](docs/guide.md) first.\n";

    private readonly DirectoryInfo repository = Directory.CreateTempSubdirectory("lockflight-references-");

    [Fact]
    public async Task ALinkTheChangeBreaksFailsWithItsFileLineAndReason()
    {
        await InitRepositoryAsync();
        File.Delete(Path.Combine(repository.FullName, "docs", "guide.md"));

        GateStep step = await RunStepAsync(GateMode.Fail);

        Assert.Equal(Verdict.Fail, step.Verdict);
        Assert.StartsWith("1 references this change broke: CLAUDE.md:3 `docs/guide.md` (no such file); fix the reference, or what it names", step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReferenceBrokenBeforeTheChangeIsListedButNotJudged()
    {
        Write("CLAUDE.md", Claude + "See [the old notes](notes/old.md).\n");
        Write("docs/guide.md", "# Guide\n");
        await InitRepositoryAsync();
        Write("CLAUDE.md", "# Project\n\nSee [the old notes](notes/old.md).\n\nRead [the guide](docs/guide.md) first.\n");

        GateStep step = await RunStepAsync(GateMode.Fail);

        Assert.Equal(Verdict.Pass, step.Verdict);
        Assert.Equal("1 instruction files checked; 1 references broken before this change, not judged: CLAUDE.md:3 `notes/old.md` (no such file)", step.Detail);
    }

    [Fact]
    public async Task WithABaseACommittedRenameThatStrandsACodeSpanPathFails()
    {
        Write(".claude/rules/testing.md", "Run the tests with `scripts/test.sh`.\n");
        Write("scripts/test.sh", "dotnet test\n");
        await InitRepositoryAsync();
        await GitAsync(["branch", "base"]);
        await GitAsync(["mv", "scripts/test.sh", "scripts/run-tests.sh"]);
        await CommitAsync("rename");

        GateStep withoutBase = await RunStepAsync(GateMode.Fail);
        GateStep withBase = await RunStepAsync(GateMode.Fail, baseRef: "base");

        Assert.Equal(Verdict.Pass, withoutBase.Verdict);
        Assert.Contains("1 references broken before this change", withoutBase.Detail, StringComparison.Ordinal);
        Assert.Equal(Verdict.Fail, withBase.Verdict);
        Assert.Contains(".claude/rules/testing.md:1 `scripts/test.sh` (renamed to scripts/run-tests.sh in", withBase.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AHeadingTheChangeRenamesBreaksTheAnchorThatPointsAtIt()
    {
        Write("CLAUDE.md", "# Project\n\nSee [setup](docs/guide.md#setup).\n");
        Write("docs/guide.md", "# Guide\n\n## Setup\n");
        await InitRepositoryAsync();
        Write("docs/guide.md", "# Guide\n\n## Installation\n");

        GateStep step = await RunStepAsync(GateMode.Fail);

        Assert.Equal(Verdict.Fail, step.Verdict);
        Assert.Contains("CLAUDE.md:3 `docs/guide.md#setup` (no heading '#setup' in guide.md)", step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANewInstructionFileWithABrokenCommandLineFails()
    {
        await InitRepositoryAsync();
        Write("services/orders/AGENTS.md", "Check with `lockflight gate run --profil pr` before pushing.\n");

        GateStep step = await RunStepAsync(GateMode.Fail);

        Assert.Equal(Verdict.Fail, step.Verdict);
        Assert.Contains("services/orders/AGENTS.md:1 `lockflight gate run --profil pr`", step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABrokenLinkOutsideTheInstructionFilesIsNotThisStepsBusiness()
    {
        Write("docs/index.md", "[guide](guide.md)\n");
        await InitRepositoryAsync();
        Write("CLAUDE.md", "# Project\n");
        File.Delete(Path.Combine(repository.FullName, "docs", "guide.md"));

        GateStep step = await RunStepAsync(GateMode.Fail);

        Assert.Equal(Verdict.Pass, step.Verdict);
    }

    [Fact]
    public async Task InAProjectBelowTheRepositoryRootPathsAreTheProjects()
    {
        Write("app/CLAUDE.md", Claude);
        Write("app/docs/guide.md", "# Guide\n");
        await InitRepositoryAsync();
        await GitAsync(["branch", "base"]);
        File.Delete(Path.Combine(repository.FullName, "app", "docs", "guide.md"));
        await CommitAsync("drop the guide");

        GateStep step = await RunStepAsync(GateMode.Fail, baseRef: "base", projectRoot: Path.Combine(repository.FullName, "app"));

        Assert.Equal(Verdict.Fail, step.Verdict);
        Assert.StartsWith("1 references this change broke: CLAUDE.md:3 `docs/guide.md`", step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InReportModeTheBrokenReferencesAreShownOnAPass()
    {
        await InitRepositoryAsync();
        File.Delete(Path.Combine(repository.FullName, "docs", "guide.md"));

        GateStep step = await RunStepAsync(GateMode.Report);

        Assert.Equal(Verdict.Pass, step.Verdict);
        Assert.StartsWith("report only (gates.docs.references: report; set it to fail to block): 1 references this change broke", step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TurnedOffTheStepDoesNotRunAndSaysSo()
    {
        await InitRepositoryAsync();

        GateStep step = await RunStepAsync(GateMode.Off);

        Assert.Equal(Verdict.NotApplicable, step.Verdict);
        Assert.Equal("off by manifest (gates.docs.references: off)", step.Detail);
    }

    [Fact]
    public async Task WithoutInstructionFilesTheStepIsNotApplicable()
    {
        Write("CLAUDE.md", string.Empty);
        await InitRepositoryAsync();
        File.Delete(Path.Combine(repository.FullName, "CLAUDE.md"));

        GateStep step = await RunStepAsync(GateMode.Fail);

        Assert.Equal(Verdict.NotApplicable, step.Verdict);
    }

    [Fact]
    public async Task AnUnknownBaseIsAnErrorEvenInReportMode()
    {
        await InitRepositoryAsync();

        GateStep step = await RunStepAsync(GateMode.Report, baseRef: "no-such-branch");

        Assert.Equal(Verdict.Error, step.Verdict);
        Assert.Contains("no merge base with 'no-such-branch'", step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OutsideAGitRepositoryTheStepIsAnError()
    {
        Write("CLAUDE.md", Claude);

        GateStep step = await RunStepAsync(GateMode.Fail);

        Assert.Equal(Verdict.Error, step.Verdict);
    }

    /// <summary>The canary: a planted broken link is caught, and its harmless twin — the same link to a file that exists — passes.</summary>
    [Theory]
    [InlineData("docs/missing.md", true)]
    [InlineData("docs/guide.md", false)]
    public async Task CanaryAPlantedBrokenLinkIsCaughtAndItsHarmlessTwinPasses(string target, bool caught)
    {
        await InitRepositoryAsync();
        Write(".claude/rules/planted.md", $"Follow [this]({target.Replace("docs/", "../../docs/", StringComparison.Ordinal)}).\n");

        GateStep step = await RunStepAsync(GateMode.Fail);

        Assert.Equal(caught ? Verdict.Fail : Verdict.Pass, step.Verdict);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(repository);

    private async Task<GateStep> RunStepAsync(GateMode mode, string? baseRef = null, string? projectRoot = null)
    {
        string root = projectRoot ?? repository.FullName;
        GateSettings settings = GateSettings.Default with { DocsReferences = mode };
        GateRunContext run = new(root, root, GateProfile.PullRequest, settings, baseRef, TimeProvider.System);
        return (await new DocsReferencesStep().RunAsync(run, GateRunFacts.None, TestContext.Current.CancellationToken)).Step;
    }

    private async Task InitRepositoryAsync()
    {
        await GitAsync(["init", "-q"]);
        if (!Directory.EnumerateFiles(repository.FullName, "*.md", SearchOption.AllDirectories).Any())
        {
            Write("CLAUDE.md", Claude);
            Write("docs/guide.md", "# Guide\n");
        }

        await CommitAsync("init");
    }

    private async Task CommitAsync(string message)
    {
        await GitAsync(["add", "-A"]);
        await GitAsync(["-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qm", message]);
    }

    private void Write(string relativePath, string content)
    {
        string path = Path.Combine(repository.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private Task<ProcessResult> GitAsync(string[] arguments) =>
        ProcessRunner.RunAsync("git", arguments, repository.FullName, TestContext.Current.CancellationToken);
}
