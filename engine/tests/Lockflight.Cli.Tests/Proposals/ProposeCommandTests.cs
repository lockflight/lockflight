using System.Text;
using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Proposals;

public sealed class ProposeCommandTests : IDisposable
{
    private readonly DirectoryInfo projectRoot = Directory.CreateTempSubdirectory("lockflight-propose-");
    private readonly DirectoryInfo scratch = Directory.CreateTempSubdirectory("lockflight-scratch-");

    public ProposeCommandTests() =>
        File.WriteAllText(Path.Combine(projectRoot.FullName, Manifest.FileName), "protected:\n  - secrets/**\n");

    [Fact]
    public async Task AModifiedProtectedFileBecomesAPatchThatGitAppliesToTheNewContent()
    {
        await InitRepositoryAsync();
        WriteProject("src/Directory.Build.props", "<Project>\n  <PropertyGroup>\n    <RootNamespace>Old.Name</RootNamespace>\n  </PropertyGroup>\n</Project>\n");
        await CommitAllAsync();
        string newContent = "<Project>\n  <PropertyGroup>\n    <RootNamespace>New.Name</RootNamespace>\n  </PropertyGroup>\n</Project>\n";
        string from = WriteScratch("props.new", newContent);

        (ExitCode exitCode, string output, string error) = await ProposeAsync(["src/Directory.Build.props", "--from", from]);

        Assert.True(exitCode == ExitCode.Pass, error);
        string patch = Assert.Single(PatchFiles());
        string patchText = File.ReadAllText(patch);
        Assert.Contains("diff --git a/src/Directory.Build.props b/src/Directory.Build.props", patchText, StringComparison.Ordinal);
        Assert.Contains("--- a/src/Directory.Build.props", patchText, StringComparison.Ordinal);
        Assert.Contains("+++ b/src/Directory.Build.props", patchText, StringComparison.Ordinal);
        Assert.Contains("rule '**/Directory.Build.props'", output, StringComparison.Ordinal);
        Assert.Contains("high risk", output, StringComparison.Ordinal);
        Assert.Contains("+1 -1", output, StringComparison.Ordinal);
        string relativePatch = Path.GetRelativePath(projectRoot.FullName, patch).Replace('\\', '/');
        Assert.Contains($"git apply --check {relativePatch} && git apply {relativePatch}", output, StringComparison.Ordinal);
        Assert.Equal("<Project>\n  <PropertyGroup>\n    <RootNamespace>Old.Name</RootNamespace>\n  </PropertyGroup>\n</Project>\n", ReadProject("src/Directory.Build.props"));

        await ApplyAsync(patch);

        Assert.Equal(newContent, ReadProject("src/Directory.Build.props"));
    }

    [Fact]
    public async Task ANewFileInANestedDirectoryIsProposedAsAnAddition()
    {
        await InitRepositoryAsync();
        WriteProject("README.md", "readme\n");
        await CommitAllAsync();
        string from = WriteScratch("hook.sh", "#!/bin/sh\nexit 0\n");

        (ExitCode exitCode, string output, string error) = await ProposeAsync([".claude/hooks/deep/check.sh", "--from", from]);

        Assert.True(exitCode == ExitCode.Pass, error);
        string patch = Assert.Single(PatchFiles());
        string patchText = File.ReadAllText(patch);
        Assert.Contains("diff --git a/.claude/hooks/deep/check.sh b/.claude/hooks/deep/check.sh", patchText, StringComparison.Ordinal);
        Assert.Contains("new file mode", patchText, StringComparison.Ordinal);
        Assert.Contains("(new)", output, StringComparison.Ordinal);
        Assert.Contains("high risk", output, StringComparison.Ordinal);

        await ApplyAsync(patch);

        Assert.Equal("#!/bin/sh\nexit 0\n", ReadProject(".claude/hooks/deep/check.sh"));
    }

    [Fact]
    public async Task ADeletionIsProposedWithDelete()
    {
        await InitRepositoryAsync();
        WriteProject("global.json", "{ \"sdk\": { \"version\": \"10.0.100\" } }\n");
        await CommitAllAsync();

        (ExitCode exitCode, string output, string error) = await ProposeAsync(["global.json", "--delete"]);

        Assert.True(exitCode == ExitCode.Pass, error);
        string patch = Assert.Single(PatchFiles());
        Assert.Contains("deleted file mode", File.ReadAllText(patch), StringComparison.Ordinal);
        Assert.Contains("(deleted)", output, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(projectRoot.FullName, "global.json")));

        await ApplyAsync(patch);

        Assert.False(File.Exists(Path.Combine(projectRoot.FullName, "global.json")));
    }

    [Fact]
    public async Task CrlfLineEndingsSurviveTheRoundTrip()
    {
        await InitRepositoryAsync();
        WriteProject("src/Directory.Build.targets", "<Project>\r\n  <Target Name=\"A\" />\r\n</Project>\r\n");
        await CommitAllAsync();
        string newContent = "<Project>\r\n  <Target Name=\"B\" />\r\n</Project>\r\n";
        string from = WriteScratch("targets.new", newContent);

        (ExitCode exitCode, _, string error) = await ProposeAsync(["src/Directory.Build.targets", "--from", from]);

        Assert.True(exitCode == ExitCode.Pass, error);
        string patch = Assert.Single(PatchFiles());
        Assert.Contains("+  <Target Name=\"B\" />\r\n", File.ReadAllText(patch), StringComparison.Ordinal);

        await ApplyAsync(patch);

        Assert.Equal(newContent, ReadProject("src/Directory.Build.targets"));
    }

    [Fact]
    public async Task SeveralPairsGiveOnePatchPerFile()
    {
        WriteProject("global.json", "{}\n");
        WriteProject("src/App.cs", "class App;\n");
        string json = WriteScratch("global.new", "{ \"sdk\": {} }\n");
        string code = WriteScratch("App.new", "class App { }\n");

        (ExitCode exitCode, string output, string error) = await ProposeAsync(["global.json", "--from", json, "src/App.cs", "--from", code]);

        Assert.True(exitCode == ExitCode.Pass, error);
        Assert.Equal(2, PatchFiles().Length);
        Assert.Contains("src/App.cs (modified)", output, StringComparison.Ordinal);
        Assert.Contains("not protected", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APathOutsideTheProjectIsRefused()
    {
        string from = WriteScratch("x.new", "x\n");

        (ExitCode exitCode, _, string error) = await ProposeAsync([Path.Combine(scratch.FullName, "elsewhere.txt"), "--from", from]);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("outside the project", error, StringComparison.Ordinal);
        Assert.Empty(PatchFiles());
    }

    [Fact]
    public async Task AParentRelativePathOutOfTheProjectIsRefused()
    {
        string from = WriteScratch("x.new", "x\n");

        (ExitCode exitCode, _, string error) = await ProposeAsync(["../escape.txt", "--from", from]);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("outside the project", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContentFromInsideAProtectedPathIsRefused()
    {
        WriteProject("global.json", "{}\n");
        WriteProject("secrets/new.json", "{ \"sdk\": {} }\n");

        (ExitCode exitCode, _, string error) = await ProposeAsync(["global.json", "--from", "secrets/new.json"]);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("protected by rule 'secrets/**'", error, StringComparison.Ordinal);
        Assert.Empty(PatchFiles());
    }

    [Fact]
    public async Task BinaryContentIsRefused()
    {
        WriteProject("global.json", "{}\n");
        string from = Path.Combine(scratch.FullName, "blob.bin");
        File.WriteAllBytes(from, [0x7B, 0x00, 0x7D]);

        (ExitCode exitCode, _, string error) = await ProposeAsync(["global.json", "--from", from]);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("binary", error, StringComparison.Ordinal);
        Assert.Empty(PatchFiles());
    }

    [Fact]
    public async Task OneRefusedPairWritesNoPatchAtAll()
    {
        WriteProject("global.json", "{}\n");
        string good = WriteScratch("good.new", "{ \"sdk\": {} }\n");

        (ExitCode exitCode, _, string error) = await ProposeAsync(["global.json", "--from", good, "missing.txt", "--delete"]);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("missing.txt", error, StringComparison.Ordinal);
        Assert.Empty(PatchFiles());
    }

    [Fact]
    public async Task IdenticalContentIsRefusedAsNothingToPropose()
    {
        WriteProject("global.json", "{}\n");
        string from = WriteScratch("same.json", "{}\n");

        (ExitCode exitCode, _, string error) = await ProposeAsync(["global.json", "--from", from]);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("already has this content", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new string[0], "name a project file")]
    [InlineData(new[] { "global.json" }, "needs --from <file> or --delete")]
    [InlineData(new[] { "global.json", "--from" }, "--from needs a value")]
    [InlineData(new[] { "global.json", "--from", "a", "--delete" }, "needs --from <file> or --delete")]
    [InlineData(new[] { "global.json", "--frm", "a" }, "unknown option '--frm'")]
    public async Task AnInvalidCommandLineIsAUsageError(string[] arguments, string message)
    {
        (ExitCode exitCode, _, string error) = await ProposeAsync(arguments);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains(message, error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSameFileNamedTwiceIsRefused()
    {
        WriteProject("global.json", "{}\n");

        (ExitCode exitCode, _, string error) = await ProposeAsync(["global.json", "--delete", "./global.json", "--delete"]);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("more than once", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheAgentMayRunItAndTheLedgerRecordsTheProposalWithoutContent()
    {
        WriteProject("src/Directory.Build.props", "<Project />\n");
        string from = WriteScratch("props.new", "<Project><!-- secret-ish content --></Project>\n");
        Dictionary<string, string> agent = new() { [EnvironmentVariables.ClaudeCode] = "1" };

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(
            ["propose", "src/Directory.Build.props", "--from", from], string.Empty, projectRoot.FullName, agent);

        Assert.True(exitCode == ExitCode.Pass, error);
        string ledger = File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(projectRoot.FullName, LedgerWriter.RelativeDirectory), "events-*.jsonl")));
        using JsonDocument line = JsonDocument.Parse(ledger.Trim());
        Assert.Equal("proposal.created", line.RootElement.GetProperty("type").GetString());
        Assert.Contains("src/Directory.Build.props", ledger, StringComparison.Ordinal);
        Assert.Contains("**/Directory.Build.props", ledger, StringComparison.Ordinal);
        Assert.Contains("high risk: yes", ledger, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-ish", ledger, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PatchesLandInTheGitIgnoredLocalDirectory()
    {
        WriteProject("notes.txt", "a\n");
        string from = WriteScratch("notes.new", "b\n");

        await ProposeAsync(["notes.txt", "--from", from]);

        string patch = Assert.Single(PatchFiles());
        Assert.Matches(@"^\d{8}-\d{6}-notes-txt\.patch$", Path.GetFileName(patch));
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

    private void WriteProject(string relativePath, string content)
    {
        string path = Path.Combine(projectRoot.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(content));
    }

    private string ReadProject(string relativePath) =>
        Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(projectRoot.FullName, relativePath)));

    private string WriteScratch(string name, string content)
    {
        string path = Path.Combine(scratch.FullName, name);
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(content));
        return path;
    }

    private async Task InitRepositoryAsync()
    {
        await GitAsync(["init", "-q", "-b", "main"]);
        // The person's own line-ending settings must not rewrite what the test compares byte for byte.
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
