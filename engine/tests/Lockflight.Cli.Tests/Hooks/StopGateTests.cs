using Lockflight.Cli.Config;
using Lockflight.Cli.Hooks;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Hooks;

public sealed class StopGateTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-stopgate-");

    [Fact]
    public async Task AStopTargetInsideAUnitBuildsTheUnitsBuildFile()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string unit = Path.Combine(root.FullName, "svc", "a");
        Directory.CreateDirectory(unit);
        // Two solutions: a bare 'dotnet build' in the unit cannot choose (MSB1011); the unit's build: names one.
        await File.WriteAllTextAsync(Path.Combine(unit, "A.slnx"), "<Solution />\n", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(unit, "B.slnx"), "<Solution />\n", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root.FullName, Manifest.FileName),
            "version: 1\nstop:\n  gate: true\n  target: svc/a\nunits:\n  - path: svc/a\n    build: A.slnx\n", cancellationToken);
        await GitAsync(["init", "-q"]);
        await GitAsync(["add", "."]);
        await GitAsync(["-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qm", "base"]);
        Manifest manifest = ManifestLocator.Discover(root.FullName).Manifest!;

        HookDecision decision = await StopGate.DecideAsync(manifest, TimeProvider.System, cancellationToken);

        Assert.DoesNotContain("MSB1011", decision.Reason ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("build=PASS", decision.Reason ?? string.Empty, StringComparison.Ordinal);
    }

    private async Task GitAsync(string[] arguments)
    {
        ProcessResult result = await ProcessRunner.RunAsync("git", arguments, root.FullName, TestContext.Current.CancellationToken);
        Assert.True(result.ExitCode == 0, result.Combined);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);
}
