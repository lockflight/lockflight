using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Gates;

/// <summary>Only a PASS of the stop or pr profile is a first green: an N/A run or an edit run is not.</summary>
public sealed class FirstGreenCommandTests : IDisposable
{
    /// <summary>A unit that is guarded but not built: the stop profile passes fast without a build.</summary>
    private const string GuardedOnly = "version: 2\nstop:\n  gate: true\nunits:\n  - path: web\n    kind: none\n";

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-first-green-command-");

    [Fact]
    public async Task ARunThatJudgedNothingIsNoFirstGreen()
    {
        await CreateProjectAsync();

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["gate", "run", "--profile", "stop"], string.Empty, root.FullName);

        Assert.True(exitCode == ExitCode.Pass, output);
        Assert.Contains("Result: N/A", output, StringComparison.Ordinal);
        Assert.DoesNotContain("First green", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEditRunIsNoFirstGreen()
    {
        await CreateProjectAsync();

        (_, string output, _) = await TestConsole.RunAsync(["gate", "run", "--profile", "edit"], string.Empty, root.FullName);

        Assert.DoesNotContain("First green", output, StringComparison.Ordinal);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private async Task CreateProjectAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(Path.Combine(root.FullName, "web"));
        await File.WriteAllTextAsync(Path.Combine(root.FullName, "web", "index.html"), "<p>hi</p>", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root.FullName, Manifest.FileName), GuardedOnly, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root.FullName, ".gitignore"), ".lockflight/\n", cancellationToken);
        await GitAsync(["init", "-q"]);
        await GitAsync(["add", "-A"]);
        await GitAsync(["-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qm", "init"]);
        await new FirstGreen(root.FullName, TimeProvider.System).RecordInitAsync(cancellationToken);
    }

    private async Task GitAsync(string[] arguments)
    {
        ProcessResult result = await ProcessRunner.RunAsync("git", arguments, root.FullName, TestContext.Current.CancellationToken);
        Assert.True(result.ExitCode == 0, result.Combined);
    }
}
