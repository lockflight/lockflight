using System.Text;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Proposals;

namespace Lockflight.Cli.Tests.Proposals;

public sealed class ProposePathEdgeCaseTests : IDisposable
{
    private readonly DirectoryInfo projectRoot = Directory.CreateTempSubdirectory("lockflight-propose-edge-");
    private readonly DirectoryInfo scratch = Directory.CreateTempSubdirectory("lockflight-scratch-edge-");

    public ProposePathEdgeCaseTests() =>
        File.WriteAllText(Path.Combine(projectRoot.FullName, Manifest.FileName), "protected:\n  - secrets/**\n");

    [Fact]
    public async Task AParentSegmentThatStaysInsideTheProjectProposesTheFileItReaches()
    {
        WriteProject("global.json", "{}\n");
        string from = WriteScratch("global.new", "{ \"sdk\": {} }\n");

        (ExitCode exitCode, string output, string error) = await ProposeAsync(["src/../global.json", "--from", from]);

        Assert.True(exitCode == ExitCode.Pass, error);
        Assert.StartsWith("global.json (modified)", output, StringComparison.Ordinal);
        Assert.Contains("diff --git a/global.json b/global.json", ReadSinglePatch(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAbsolutePathInsideTheProjectIsProposedRelativeToTheRoot()
    {
        WriteProject("src/App.cs", "class App;\n");
        string from = WriteScratch("App.new", "class App { }\n");

        (ExitCode exitCode, string output, string error) = await ProposeAsync([Path.Combine(projectRoot.FullName, "src", "App.cs"), "--from", from]);

        Assert.True(exitCode == ExitCode.Pass, error);
        Assert.StartsWith("src/App.cs (modified)", output, StringComparison.Ordinal);
        Assert.Contains("diff --git a/src/App.cs b/src/App.cs", ReadSinglePatch(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../../../../../../../../etc/passwd")]
    [InlineData(".")]
    [InlineData("src/..")]
    [InlineData("Z:\\elsewhere\\file.txt")]
    [InlineData("Z:/elsewhere/file.txt")]
    public async Task APathThatIsNotAFileInsideTheProjectIsRefused(string path)
    {
        string from = WriteScratch("x.new", "x\n");

        (ExitCode exitCode, _, string error) = await ProposeAsync([path, "--from", from]);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("outside the project", error, StringComparison.Ordinal);
        AssertNothingWritten();
    }

    [Fact]
    public async Task WindowsSeparatorsNameTheSameProjectFile()
    {
        WriteProject("src/App.cs", "class App;\n");
        string from = WriteScratch("App.new", "class App { }\n");

        (ExitCode exitCode, string output, string error) = await ProposeAsync(["src\\App.cs", "--from", from]);

        Assert.True(exitCode == ExitCode.Pass, error);
        Assert.StartsWith("src/App.cs (modified)", output, StringComparison.Ordinal);
        Assert.Contains("diff --git a/src/App.cs b/src/App.cs", ReadSinglePatch(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASymbolicLinkToAFileOutsideTheProjectIsRefused()
    {
        Assert.SkipUnless(SymbolicLinks.CanCreate, SymbolicLinks.SkipReason);
        string outside = WriteScratch("outside.txt", "outside\n");
        File.CreateSymbolicLink(Path.Combine(projectRoot.FullName, "notes.txt"), outside);
        string from = WriteScratch("notes.new", "changed\n");

        (ExitCode exitCode, _, string error) = await ProposeAsync(["notes.txt", "--from", from]);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("outside the project", error, StringComparison.Ordinal);
        AssertNothingWritten();
    }

    [Fact]
    public async Task ADirectoryLinkOutOfTheProjectIsRefused()
    {
        Assert.SkipUnless(SymbolicLinks.CanCreate, SymbolicLinks.SkipReason);
        Directory.CreateSymbolicLink(Path.Combine(projectRoot.FullName, "vendor"), scratch.FullName);
        string from = WriteScratch("lib.new", "lib\n");

        (ExitCode exitCode, _, string error) = await ProposeAsync(["vendor/lib.txt", "--from", from]);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("outside the project", error, StringComparison.Ordinal);
        AssertNothingWritten();
    }

    [Fact]
    public async Task ALinkIntoAProtectedPathProposesTheRealFileUnderItsRule()
    {
        Assert.SkipUnless(SymbolicLinks.CanCreate, SymbolicLinks.SkipReason);
        await InitRepositoryAsync();
        WriteProject("secrets/key.txt", "old\n");
        await CommitAllAsync();
        File.CreateSymbolicLink(Path.Combine(projectRoot.FullName, "notes.txt"), Path.Combine(projectRoot.FullName, "secrets", "key.txt"));
        string from = WriteScratch("key.new", "new\n");

        (ExitCode exitCode, string output, string error) = await ProposeAsync(["notes.txt", "--from", from]);

        Assert.True(exitCode == ExitCode.Pass, error);
        Assert.StartsWith("secrets/key.txt (modified)", output, StringComparison.Ordinal);
        Assert.Contains("protected by rule 'secrets/**'", output, StringComparison.Ordinal);
        string patch = Assert.Single(PatchFiles());
        Assert.Contains("diff --git a/secrets/key.txt b/secrets/key.txt", File.ReadAllText(patch), StringComparison.Ordinal);

        await ApplyAsync(patch);

        Assert.Equal("new\n", File.ReadAllText(Path.Combine(projectRoot.FullName, "secrets", "key.txt")));
    }

    [Fact]
    public async Task AFileNamedThroughALinkAndByItsOwnNameIsNamedTwice()
    {
        Assert.SkipUnless(SymbolicLinks.CanCreate, SymbolicLinks.SkipReason);
        WriteProject("notes.txt", "a\n");
        File.CreateSymbolicLink(Path.Combine(projectRoot.FullName, "alias.txt"), Path.Combine(projectRoot.FullName, "notes.txt"));
        string first = WriteScratch("first.new", "b\n");
        string second = WriteScratch("second.new", "c\n");

        (ExitCode exitCode, _, string error) = await ProposeAsync(["notes.txt", "--from", first, "alias.txt", "--from", second]);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("'notes.txt' is named more than once", error, StringComparison.Ordinal);
        AssertNothingWritten();
    }

    [Theory]
    [InlineData(".git/config")]
    [InlineData(".GIT/config")]
    [InlineData(".Git/hooks/pre-commit")]
    [InlineData("sub/.git/HEAD")]
    [InlineData(".git")]
    public async Task GitDataIsRefusedInAnyCase(string path)
    {
        Directory.CreateDirectory(Path.Combine(projectRoot.FullName, ".git", "hooks"));
        File.WriteAllText(Path.Combine(projectRoot.FullName, ".git", "config"), "[core]\n");
        string from = WriteScratch("config.new", "[core]\n\tbare = false\n");

        (ExitCode exitCode, _, string error) = await ProposeAsync([path, "--from", from]);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.True(error.Contains("git's own data", StringComparison.Ordinal) || error.Contains("is a directory", StringComparison.Ordinal), error);
        AssertNothingWritten();
    }

    [Fact]
    public async Task AWorktreeGitFileIsRefusedAsGitData()
    {
        WriteProject(".git", "gitdir: /elsewhere/.git/worktrees/feature\n");
        string from = WriteScratch("git.new", "gitdir: /tmp/other\n");

        (ExitCode exitCode, _, string error) = await ProposeAsync([".git", "--from", from]);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("git's own data", error, StringComparison.Ordinal);
        AssertNothingWritten();
    }

    [Fact]
    public async Task AnotherCaseOfAnExistingFileProposesTheStoredName()
    {
        WriteProject("src/App.cs", "class App;\n");
        Assert.SkipUnless(File.Exists(Path.Combine(projectRoot.FullName, "SRC", "app.cs")), "The file system is case-sensitive.");
        string from = WriteScratch("App.new", "class App { }\n");

        (ExitCode exitCode, string output, string error) = await ProposeAsync(["SRC/app.cs", "--from", from]);

        Assert.True(exitCode == ExitCode.Pass, error);
        Assert.StartsWith("src/App.cs (modified)", output, StringComparison.Ordinal);
        Assert.Contains("diff --git a/src/App.cs b/src/App.cs", ReadSinglePatch(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADecomposedSpellingIsProposedAsTypedSoThePatchNamesThePathShown()
    {
        string decomposed = "docs/cafe\u0301.md";
        WriteProject("docs/caf\u00e9.md", "old\n");
        Assert.SkipUnless(File.Exists(Path.Combine(projectRoot.FullName, decomposed)), "The file system tells the two normal forms apart.");
        string from = WriteScratch("cafe.new", "new\n");

        (ExitCode exitCode, string output, string error) = await ProposeAsync([decomposed, "--from", from]);

        Assert.True(exitCode == ExitCode.Pass, error);
        Assert.StartsWith($"{decomposed} (modified)", output, StringComparison.Ordinal);
        Assert.Contains($"diff --git a/{decomposed} b/{decomposed}", ReadSinglePatch(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANameWithSpacesRoundTripsThroughGitApply()
    {
        await InitRepositoryAsync();
        WriteProject("my docs/read me.md", "old\n");
        await CommitAllAsync();
        string from = WriteScratch("readme.new", "new\n");

        (ExitCode exitCode, string output, string error) = await ProposeAsync(["my docs/read me.md", "--from", from]);

        Assert.True(exitCode == ExitCode.Pass, error);
        Assert.StartsWith("my docs/read me.md (modified)", output, StringComparison.Ordinal);
        string patch = Assert.Single(PatchFiles());
        Assert.Contains("diff --git a/my docs/read me.md b/my docs/read me.md", File.ReadAllText(patch), StringComparison.Ordinal);

        await ApplyAsync(patch);

        Assert.Equal("new\n", File.ReadAllText(Path.Combine(projectRoot.FullName, "my docs", "read me.md")));
    }

    [Theory]
    [InlineData("new\nline.txt")]
    [InlineData("color\u001b[31m.txt")]
    [InlineData("title\u001b]0;pwned\u0007.txt")]
    [InlineData("back\rspace.txt")]
    [InlineData("txt.\u202eexe")]
    public async Task ANameWithAControlCharacterIsRefused(string name)
    {
        string from = WriteScratch("x.new", "x\n");

        (ExitCode exitCode, _, string error) = await ProposeAsync([name, "--from", from]);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("control character", error, StringComparison.Ordinal);
        Assert.DoesNotContain('\u001b', error);
        AssertNothingWritten();
    }

    [Fact]
    public async Task ALinkToAFileWithAControlCharacterInItsNameIsRefused()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows file names cannot hold control characters.");
        WriteProject("odd\u001b[2Jname.txt", "a\n");
        File.CreateSymbolicLink(Path.Combine(projectRoot.FullName, "plain.txt"), Path.Combine(projectRoot.FullName, "odd\u001b[2Jname.txt"));
        string from = WriteScratch("x.new", "b\n");

        (ExitCode exitCode, _, string error) = await ProposeAsync(["plain.txt", "--from", from]);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("control character", error, StringComparison.Ordinal);
        AssertNothingWritten();
    }

    [Fact]
    public async Task ANameThatLooksLikeAnOptionIsStillAPathForGit()
    {
        await InitRepositoryAsync();
        WriteProject("-old.txt", "gone\n");
        await CommitAllAsync();
        string from = WriteScratch("dash.new", "dash\n");

        (ExitCode exitCode, _, string error) = await ProposeAsync(["./-new.txt", "--from", from, "./-old.txt", "--delete"]);

        Assert.True(exitCode == ExitCode.Pass, error);
        string[] patches = PatchFiles();
        Assert.Equal(2, patches.Length);
        foreach (string patch in patches)
        {
            await ApplyAsync(patch);
        }

        Assert.Equal("dash\n", File.ReadAllText(Path.Combine(projectRoot.FullName, "-new.txt")));
        Assert.False(File.Exists(Path.Combine(projectRoot.FullName, "-old.txt")));
    }

    [Fact]
    public async Task AVeryLongNameFailsWithoutWritingAPatch()
    {
        string name = new string('n', 300) + ".txt";
        string from = WriteScratch("long.new", "x\n");

        (ExitCode exitCode, string output, string error) = await ProposeAsync([name, "--from", from]);

        Assert.NotEqual(ExitCode.Pass, exitCode);
        Assert.DoesNotContain("internal error", error, StringComparison.Ordinal);
        Assert.Empty(output);
        AssertNothingWritten();
    }

    [Fact]
    public async Task AProposalsDirectoryThatIsALinkIsRefused()
    {
        Assert.SkipUnless(SymbolicLinks.CanCreate, SymbolicLinks.SkipReason);
        WriteProject("notes.txt", "a\n");
        Directory.CreateDirectory(Path.Combine(projectRoot.FullName, ".lockflight", "local"));
        DirectoryInfo elsewhere = Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "secrets"));
        Directory.CreateSymbolicLink(Path.Combine(projectRoot.FullName, ".lockflight", "local", "proposals"), elsewhere.FullName);
        string from = WriteScratch("notes.new", "b\n");

        (ExitCode exitCode, _, string error) = await ProposeAsync(["notes.txt", "--from", from]);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains(".lockflight/local/proposals is a symbolic link, so a patch cannot be written safely", error, StringComparison.Ordinal);
        Assert.Empty(elsewhere.GetFiles());
        Assert.False(Directory.Exists(Path.Combine(projectRoot.FullName, LedgerWriter.RelativeDirectory)));
    }

    [Fact]
    public async Task ALockflightDirectoryThatIsALinkIsRefused()
    {
        Assert.SkipUnless(SymbolicLinks.CanCreate, SymbolicLinks.SkipReason);
        WriteProject("notes.txt", "a\n");
        Directory.CreateSymbolicLink(Path.Combine(projectRoot.FullName, ".lockflight"), scratch.FullName);
        string from = WriteScratch("notes.new", "b\n");

        (ExitCode exitCode, _, string error) = await ProposeAsync(["notes.txt", "--from", from]);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains(".lockflight is a symbolic link", error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(scratch.FullName, "local")));
    }

    [Fact]
    public async Task AProposalsDirectoryThatIsAFileIsRefused()
    {
        WriteProject("notes.txt", "a\n");
        WriteProject(".lockflight/local/proposals", "not a directory\n");
        string from = WriteScratch("notes.new", "b\n");

        (ExitCode exitCode, _, string error) = await ProposeAsync(["notes.txt", "--from", from]);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("is a file, not a directory", error, StringComparison.Ordinal);
        Assert.Equal("not a directory\n", File.ReadAllText(Path.Combine(projectRoot.FullName, ".lockflight", "local", "proposals")));
    }

    [Fact]
    public async Task ContentLinkedFromAProtectedFileIsRefused()
    {
        Assert.SkipUnless(SymbolicLinks.CanCreate, SymbolicLinks.SkipReason);
        WriteProject("global.json", "{}\n");
        WriteProject("secrets/token.json", "{ \"token\": \"t\" }\n");
        string link = Path.Combine(scratch.FullName, "innocent.json");
        File.CreateSymbolicLink(link, Path.Combine(projectRoot.FullName, "secrets", "token.json"));

        (ExitCode exitCode, _, string error) = await ProposeAsync(["global.json", "--from", link]);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("protected by rule 'secrets/**'", error, StringComparison.Ordinal);
        AssertNothingWritten();
    }

    [Fact]
    public async Task ContentFromADeviceThatNeverEndsIsRefused()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Uses /dev/zero.");
        WriteProject("notes.txt", "a\n");

        (ExitCode exitCode, _, string error) = await ProposeAsync(["notes.txt", "--from", "/dev/zero"]);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("larger than", error, StringComparison.Ordinal);
        AssertNothingWritten();
    }

    [Fact]
    public async Task ContentLargerThanTheLimitIsRefused()
    {
        WriteProject("notes.txt", "a\n");
        string from = Path.Combine(scratch.FullName, "huge.txt");
        File.WriteAllBytes(from, Enumerable.Repeat((byte)'x', ProposalTargets.MaxContentBytes + 1).ToArray());

        (ExitCode exitCode, _, string error) = await ProposeAsync(["notes.txt", "--from", from]);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("larger than", error, StringComparison.Ordinal);
        AssertNothingWritten();
    }

    [Fact]
    public async Task OneRefusedLinkAmongGoodPairsWritesNothingAtAll()
    {
        Assert.SkipUnless(SymbolicLinks.CanCreate, SymbolicLinks.SkipReason);
        WriteProject("global.json", "{}\n");
        File.CreateSymbolicLink(Path.Combine(projectRoot.FullName, "escape.txt"), WriteScratch("target.txt", "t\n"));
        string good = WriteScratch("good.new", "{ \"sdk\": {} }\n");
        string other = WriteScratch("other.new", "u\n");

        (ExitCode exitCode, string output, string error) = await ProposeAsync(["global.json", "--from", good, "escape.txt", "--from", other]);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("no patch was written", error, StringComparison.Ordinal);
        Assert.Empty(output);
        AssertNothingWritten();
        Assert.Equal("t\n", File.ReadAllText(Path.Combine(scratch.FullName, "target.txt")));
    }

    [Fact]
    public void ANewPatchPathSkipsADanglingLinkAlreadyThere()
    {
        Assert.SkipUnless(SymbolicLinks.CanCreate, SymbolicLinks.SkipReason);
        DateTimeOffset now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);
        string first = ProposalFiles.NewPath(projectRoot.FullName, now, "notes.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(first)!);
        File.CreateSymbolicLink(first, Path.Combine(scratch.FullName, "missing.patch"));

        string second = ProposalFiles.NewPath(projectRoot.FullName, now, "notes.txt");

        Assert.NotEqual(first, second);
        Assert.EndsWith("-notes-txt-2.patch", second, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        TempDirectories.DeleteRecursively(projectRoot);
        TempDirectories.DeleteRecursively(scratch);
    }

    private Task<(ExitCode ExitCode, string Output, string Error)> ProposeAsync(string[] arguments) =>
        TestConsole.RunAsync(["propose", .. arguments], string.Empty, projectRoot.FullName);

    private string[] PatchFiles()
    {
        string directory = Path.Combine(projectRoot.FullName, ".lockflight", "local", "proposals");
        return Directory.Exists(directory) ? Directory.GetFiles(directory) : [];
    }

    private string ReadSinglePatch() => File.ReadAllText(Assert.Single(PatchFiles()));

    private void AssertNothingWritten()
    {
        Assert.Empty(PatchFiles());
        Assert.False(Directory.Exists(Path.Combine(projectRoot.FullName, LedgerWriter.RelativeDirectory)));
    }

    private void WriteProject(string relativePath, string content)
    {
        string path = Path.Combine(projectRoot.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(content));
    }

    private string WriteScratch(string name, string content)
    {
        string path = Path.Combine(scratch.FullName, name);
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(content));
        return path;
    }

    private async Task InitRepositoryAsync()
    {
        await GitAsync(["init", "-q", "-b", "main"]);
        await GitAsync(["config", "core.autocrlf", "false"]);
    }

    private async Task CommitAllAsync()
    {
        await GitAsync(["add", "-A"]);
        await GitAsync(["-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qm", "base"]);
    }

    private async Task ApplyAsync(string patch)
    {
        ProcessResult check = await GitAsync(["apply", "--check", patch]);
        Assert.True(check.ExitCode == 0, check.Combined);
        ProcessResult apply = await GitAsync(["apply", patch]);
        Assert.True(apply.ExitCode == 0, apply.Combined);
    }

    private Task<ProcessResult> GitAsync(string[] arguments) =>
        ProcessRunner.RunAsync("git", arguments, projectRoot.FullName, TestContext.Current.CancellationToken);
}
