using Lockflight.Cli.ApprovalsOfRecord;
using Lockflight.Cli.Config;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.ApprovalsOfRecord;

public sealed class ApprovalsCheckerTests : IDisposable
{
    private readonly DirectoryInfo repository = Directory.CreateTempSubdirectory("lockflight-approvals-");

    [Fact]
    public async Task CodeownersOnTheBaseThatOwnsTheSpecsSatisfiesTheSamePrDefault()
    {
        string baseCommit = await CommitAsync(new() { [".github/CODEOWNERS"] = "/specs/ @org/product\n", ["specs/001-a/spec.md"] = "a" });
        string head = await CommitAsync(new() { ["specs/002-b/spec.md"] = "b", ["src/B.cs"] = "class B;" });

        ApprovalsFacts facts = await CheckAsync(baseCommit, head, ApprovalSettings.Default, "002-b", ["specs/002-b/spec.md", "src/B.cs"]);

        Assert.True(facts.Satisfied, string.Join("; ", facts.Problems));
        Assert.Equal(".github/CODEOWNERS", facts.Codeowners.File);
        Assert.Equal(["specs/**"], facts.Codeowners.Covers);
        Assert.False(facts.SpecFirst.Applies);
    }

    [Fact]
    public async Task ASpecPathWithoutAnOwnerIsNamed()
    {
        string baseCommit = await CommitAsync(new() { ["CODEOWNERS"] = "/specs/ @org/product\n/specs/drafts/\n", ["specs/drafts/x/spec.md"] = "x" });

        ApprovalsFacts facts = await CheckAsync(baseCommit, baseCommit, ApprovalSettings.Default, featureId: null, []);

        Assert.Equal(["specs/** (specs/drafts/x/spec.md has no owner)"], facts.Codeowners.Missing);
        Assert.Contains("CODEOWNERS gives no owner to specs/** (specs/drafts/x/spec.md has no owner)", Assert.Single(facts.Problems), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFileGlobOwnsTheSpecsWithoutAMadeUpProbe()
    {
        string baseCommit = await CommitAsync(new() { [".github/CODEOWNERS"] = "/specs/**/*.md @org/product\n" });

        ApprovalsFacts empty = await CheckAsync(baseCommit, baseCommit, ApprovalSettings.Default, featureId: null, []);
        ApprovalsFacts unknownBase = await CheckAsync(new string('a', 40), baseCommit, ApprovalSettings.Default, featureId: null, []);

        Assert.True(empty.Satisfied, string.Join("; ", empty.Problems));
        Assert.Contains("git cannot read the base commit", Assert.Single(unknownBase.Problems), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACodeownersFileTheHeadAddsDoesNotCount()
    {
        string baseCommit = await CommitAsync(new() { ["README.md"] = "x" });
        string head = await CommitAsync(new() { [".github/CODEOWNERS"] = "* @me\n" });

        ApprovalsFacts facts = await CheckAsync(baseCommit, head, ApprovalSettings.Default, featureId: null, [".github/CODEOWNERS"]);

        Assert.Null(facts.Codeowners.File);
        Assert.Contains("the base branch has no CODEOWNERS file", Assert.Single(facts.Problems), StringComparison.Ordinal);
    }

    [Fact]
    public async Task APlatformWithoutCodeownersSaysSoInsteadOfPassing()
    {
        string baseCommit = await CommitAsync(new() { ["CODEOWNERS"] = "* @org/all\n" });

        ApprovalsFacts facts = await ApprovalsChecker.CheckAsync(
            new ApprovalsRequest(repository.FullName, baseCommit, baseCommit, ApprovalSettings.Default, ChangeClass.Fix, null, [], CodeownersPlatform.None), TestContext.Current.CancellationToken);

        Assert.Contains("this platform reads no CODEOWNERS file", Assert.Single(facts.Problems), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SpecFirstNeedsTheSpecOnTheBaseUnchanged()
    {
        ApprovalSettings specFirst = ApprovalSettings.Default with { Mode = ApprovalMode.SpecFirst };
        string baseCommit = await CommitAsync(new() { ["CODEOWNERS"] = "* @org/all\n", ["specs/001-a/spec.md"] = "approved" });
        string unchanged = await CommitAsync(new() { ["src/A.cs"] = "class A;" });
        string changed = await CommitAsync(new() { ["specs/001-a/spec.md"] = "edited" });

        ApprovalsFacts merged = await CheckAsync(baseCommit, unchanged, specFirst, "001-a", ["src/A.cs"]);
        ApprovalsFacts edited = await CheckAsync(baseCommit, changed, specFirst, "001-a", ["src/A.cs", "specs/001-a/spec.md"]);
        ApprovalsFacts notMerged = await CheckAsync(baseCommit, unchanged, specFirst, "002-b", ["src/A.cs"]);
        ApprovalsFacts noFeature = await CheckAsync(baseCommit, unchanged, specFirst, featureId: null, ["src/A.cs"]);

        Assert.True(merged.Satisfied, string.Join("; ", merged.Problems));
        Assert.Equal(["approvals.default=spec-first"], merged.SpecFirst.Because);
        Assert.Contains("this pull request changes specs/001-a/spec.md", Assert.Single(edited.Problems), StringComparison.Ordinal);
        Assert.Contains("this pull request changes specs/003-c/spec.md", Assert.Single((await CheckAsync(baseCommit, unchanged, specFirst, "001-a", ["src/A.cs", "specs/003-c/spec.md"])).Problems), StringComparison.Ordinal);
        Assert.Contains("specs/002-b/spec.md is not on the base branch", Assert.Single(notMerged.Problems), StringComparison.Ordinal);
        Assert.Contains("no feature spec was found", Assert.Single(noFeature.Problems), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SpecFirstAppliesFromAClassOrAPathGlob()
    {
        ApprovalSettings settings = ApprovalSettings.Default with { SpecFirstFrom = ChangeClass.Critical, SpecFirstPaths = ["src/Payments/**"] };
        string baseCommit = await CommitAsync(new() { ["CODEOWNERS"] = "* @org/all\n" });

        ApprovalsFacts byClass = await CheckAsync(baseCommit, baseCommit, settings, "001-a", ["src/A.cs"], ChangeClass.Critical);
        ApprovalsFacts byPath = await CheckAsync(baseCommit, baseCommit, settings, "001-a", ["src/Payments/Pay.cs"], ChangeClass.Behaviour);
        ApprovalsFacts neither = await CheckAsync(baseCommit, baseCommit, settings, "001-a", ["src/A.cs"], ChangeClass.Feature);

        Assert.Equal(["specFirstFrom=critical (class critical)"], byClass.SpecFirst.Because);
        Assert.Equal(["specFirstPaths src/Payments/** (src/Payments/Pay.cs)"], byPath.SpecFirst.Because);
        Assert.False(neither.SpecFirst.Applies);
        Assert.True(neither.Satisfied);
    }

    private Task<ApprovalsFacts> CheckAsync(
        string baseCommit, string head, ApprovalSettings settings, string? featureId, IReadOnlyList<string> changed, ChangeClass changeClass = ChangeClass.Behaviour) =>
        ApprovalsChecker.CheckAsync(
            new ApprovalsRequest(repository.FullName, baseCommit, head, settings, changeClass, featureId, changed, CodeownersPlatform.GitHub), TestContext.Current.CancellationToken);

    private async Task<string> CommitAsync(Dictionary<string, string> files)
    {
        if (!Directory.Exists(Path.Combine(repository.FullName, ".git")))
        {
            await GitAsync(["init", "-q"]);
        }

        foreach ((string path, string content) in files)
        {
            string full = Path.Combine(repository.FullName, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        await GitAsync(["add", "-A"]);
        await GitAsync(["-c", "user.name=t", "-c", "user.email=t@example.com", "commit", "-q", "-m", "c"]);
        return (await Git.CommitOfAsync(repository.FullName, "HEAD", TestContext.Current.CancellationToken))!;
    }

    private async Task GitAsync(string[] arguments)
    {
        ProcessResult result = await ProcessRunner.RunAsync("git", arguments, repository.FullName, TestContext.Current.CancellationToken);
        Assert.True(result.ExitCode == 0, result.Combined);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(repository);
}
