using Lockflight.Cli.Doctor;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Doctor;

public sealed class DocsChecksTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-doctor-docs-");

    [Fact]
    public async Task AnUnresolvedReferenceIsAWarningThatNamesTheFirstOne()
    {
        await CommitAsync("CLAUDE.md", "Read [the guide](docs/guide.md).\n");

        DoctorCheck drift = Assert.Single(await DocsChecks.RunAsync(root.FullName, TestContext.Current.CancellationToken), check => check.Name == "docs drift");

        Assert.Equal(CheckStatus.Warn, drift.Status);
        Assert.Contains("CLAUDE.md:1 docs/guide.md", drift.Detail, StringComparison.Ordinal);
        Assert.Contains("docs check", drift.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DocumentsThatResolveAndFitTheBudgetPass()
    {
        await CommitAsync("CLAUDE.md", "# Project\n");

        IReadOnlyList<DoctorCheck> checks = await DocsChecks.RunAsync(root.FullName, TestContext.Current.CancellationToken);

        Assert.All(checks, check => Assert.Equal(CheckStatus.Pass, check.Status));
        Assert.Equal(["docs drift", "instructions size"], checks.Select(check => check.Name));
    }

    [Fact]
    public async Task ALongClaudeMdIsASizeWarningNotADriftWarning()
    {
        await CommitAsync("CLAUDE.md", string.Concat(Enumerable.Range(1, 210).Select(index => $"- rule {index}\n")));

        IReadOnlyList<DoctorCheck> checks = await DocsChecks.RunAsync(root.FullName, TestContext.Current.CancellationToken);

        Assert.Equal(CheckStatus.Pass, checks.Single(check => check.Name == "docs drift").Status);
        DoctorCheck size = checks.Single(check => check.Name == "instructions size");
        Assert.Equal(CheckStatus.Warn, size.Status);
        Assert.Contains("CLAUDE.md: 210 lines", size.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OutsideAGitRepositoryThereAreNoDocsChecks()
    {
        File.WriteAllText(Path.Combine(root.FullName, "CLAUDE.md"), "[gone](gone.md)\n");

        Assert.Empty(await DocsChecks.RunAsync(root.FullName, TestContext.Current.CancellationToken));
    }

    private async Task CommitAsync(string relativePath, string content)
    {
        File.WriteAllText(Path.Combine(root.FullName, relativePath), content);
        await GitAsync(["init", "--quiet", "--initial-branch=main"]);
        await GitAsync(["add", "--all"]);
        await GitAsync(["-c", "user.name=Test", "-c", "user.email=test@example.com", "commit", "--quiet", "-m", "docs"]);
    }

    private async Task GitAsync(string[] arguments)
    {
        ProcessResult result = await ProcessRunner.RunAsync("git", arguments, root.FullName, TestContext.Current.CancellationToken);
        Assert.True(result.ExitCode == 0, result.Combined);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);
}
