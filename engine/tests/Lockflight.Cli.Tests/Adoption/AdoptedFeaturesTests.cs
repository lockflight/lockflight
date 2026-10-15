using Lockflight.Cli.Gates.Baselines;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Adoption;

public sealed class AdoptedFeaturesTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-adopted-");

    [Fact]
    public async Task AChangeToAnAdoptedFeatureNeedsNoApprovalButANewFeatureDoes()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Write("lockflight.yaml", "version: 1\n");
        Write("specs/001-old/spec.md", "old\n");
        new AdoptedFeatures(root.FullName).Save(["001-old"]);
        await GitAsync(["init", "-q", "-b", "main"]);
        await GitAsync(["add", "."]);
        await GitAsync(["-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qm", "base"]);
        await GitAsync(["checkout", "-qb", "work"]);
        Write("specs/001-old/spec.md", "old, edited\n");
        Write("specs/002-new/spec.md", "new\n");
        await GitAsync(["add", "."]);
        await GitAsync(["-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qm", "work"]);

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["approve", "verify", "--base", "main"], string.Empty, root.FullName);

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains("NOTE 001-old: predates Lockflight (adopted)", output, StringComparison.Ordinal);
        Assert.Contains("FAIL 002-new", output, StringComparison.Ordinal);
        Assert.DoesNotContain("FAIL 001-old", output, StringComparison.Ordinal);
    }

    private void Write(string relative, string text)
    {
        string path = Path.Combine(root.FullName, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private async Task GitAsync(string[] arguments)
    {
        ProcessResult result = await ProcessRunner.RunAsync("git", arguments, root.FullName, TestContext.Current.CancellationToken);
        Assert.True(result.ExitCode == 0, result.Combined);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);
}
