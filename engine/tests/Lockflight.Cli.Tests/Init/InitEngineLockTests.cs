using Lockflight.Cli.EngineLocks;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Init;

namespace Lockflight.Cli.Tests.Init;

public sealed class InitEngineLockTests : IDisposable
{
    private readonly DirectoryInfo projectRoot = Directory.CreateTempSubdirectory("lockflight-init-lock-");

    [Fact]
    public async Task InitSaysWhyItWroteNoEngineLock()
    {
        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["init", "--no-link"], "", projectRoot.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains("note: no engine lock written (a local build", output, StringComparison.Ordinal);
        Assert.False(File.Exists(EngineLockFiles.CommittedPath(projectRoot.FullName)));
    }

    [Fact]
    public async Task AProjectWithACommittedLockGetsTheInstallScriptsLineEndingRule()
    {
        EngineLockFiles.Write(
            EngineLockFiles.CommittedPath(projectRoot.FullName),
            new EngineLock("0.3.0", EngineLockChannel.Release, InstallScript.Sha256, EngineRids.Released.ToDictionary(rid => rid, _ => new string('a', 64), StringComparer.Ordinal)));

        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["init", "--no-link"], "", projectRoot.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains(GitAttributesUpdater.InstallScriptRule, File.ReadAllLines(Path.Combine(projectRoot.FullName, ".gitattributes")));
    }

    public void Dispose() => TempDirectories.DeleteRecursively(projectRoot);
}
