using System.IO.Compression;
using System.Text;
using Lockflight.Cli.Doctor;
using Lockflight.Cli.EngineLocks;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.EngineLocks;

public sealed class VerifyLockTests : IDisposable
{
    private const string Version = "0.0.0-ci.7";

    private readonly DirectoryInfo work = Directory.CreateTempSubdirectory("lockflight-verify-lock-");

    [Fact]
    public async Task ALockThatMatchesItsReleaseIsSaidToMatchIt()
    {
        WriteRelease(rid => Hash(rid));
        string lockFile = WriteLock(rid => Hash(rid));

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["engine", "verify-lock", lockFile, "--source", Feed], "", work.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains("matches the published hashes of release v0.0.0-ci.7", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALockWithAnotherHashThanItsReleaseFails()
    {
        WriteRelease(rid => Hash(rid));
        string lockFile = WriteLock(rid => rid == "win-x64" ? new string('0', 64) : Hash(rid));

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["engine", "verify-lock", lockFile, "--source", Feed], "", work.FullName);

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains("its hashes for win-x64 are not the ones release v0.0.0-ci.7 published", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALockForAVersionWithoutAReleaseFails()
    {
        string lockFile = WriteLock(rid => Hash(rid));

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["engine", "verify-lock", lockFile, "--source", Feed], "", work.FullName);

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains("no published hashes for 0.0.0-ci.7", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAttestationThatDoesNotVerifyFails()
    {
        WriteRelease(rid => Hash(rid));
        string lockFile = WriteLock(rid => Hash(rid));

        LockReleaseCheck check = await LockReleaseCheck.RunAsync(
            File.ReadAllText(lockFile), new EnginePackageFeed(Feed, EnvironmentReader.Of(new Dictionary<string, string>())),
            (_, _, _, _) => Task.FromResult(new Lockflight.Cli.Processes.ProcessResult(1, string.Empty, "no attestations found", 0, TimedOut: false)),
            TestContext.Current.CancellationToken);

        Assert.Equal(LockReleaseVerdict.Unattested, check.Verdict);
        Assert.Contains("matches the published hashes", check.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void CodeownersThatCoverTheLockTheManifestAndTheCiFilesPass()
    {
        WriteProject("/lockflight.yaml @team/leads\n/.lockflight/ @team/leads\n/.github/ @team/leads\n");

        DoctorCheck check = CodeownersCheck.Check(work.FullName)!;

        Assert.Equal(CheckStatus.Pass, check.Status);
        Assert.Contains("Require review from Code Owners", check.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void CodeownersThatMissTheLockAreAWarningNamingIt()
    {
        WriteProject("lockflight.yaml @team/leads\n.github/ @team/leads\n/.lockflight/engine.lock\n");

        DoctorCheck check = CodeownersCheck.Check(work.FullName)!;

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Contains(".lockflight/engine.lock", check.Detail, StringComparison.Ordinal);
        Assert.Contains(".lockflight/ci/install.sh", check.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void NoCodeownersIsAWarning()
    {
        WriteProject(codeowners: null);

        Assert.Equal(CheckStatus.Warn, CodeownersCheck.Check(work.FullName)!.Status);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(work);

    private string Feed => Path.Combine(work.FullName, "feed");

    private static string Hash(string rid) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(rid)));

    private void WriteProject(string? codeowners)
    {
        File.WriteAllText(Path.Combine(work.FullName, "lockflight.yaml"), "version: 2\n");
        WriteLock(rid => Hash(rid), EngineLockFiles.CommittedPath(work.FullName));
        Directory.CreateDirectory(Path.Combine(work.FullName, ".github", "workflows"));
        File.WriteAllText(Path.Combine(work.FullName, ".github", "workflows", "lockflight-gates.yml"), "name: x\n");
        if (codeowners is not null)
        {
            File.WriteAllText(Path.Combine(work.FullName, ".github", "CODEOWNERS"), codeowners);
        }
    }

    private string WriteLock(Func<string, string> hash, string? path = null)
    {
        path ??= Path.Combine(work.FullName, "proposed.lock");
        EngineLockFiles.Write(path, new EngineLock(Version, EngineLockChannel.Release, new string('5', 64), EngineRids.Released.ToDictionary(rid => rid, hash, StringComparer.Ordinal)));
        return path;
    }

    private void WriteRelease(Func<string, string> hash)
    {
        string binaries = string.Join(", ", EngineRids.Released.Select(rid => $"\"{rid}\": {{ \"sha256\": \"{hash(rid)}\", \"path\": \"tools/any/{rid}/{EngineRids.ExecutableName(rid)}\" }}"));
        string package = Path.Combine(Feed, "lockflight.cli", Version, $"lockflight.cli.{Version}.nupkg");
        Directory.CreateDirectory(Path.GetDirectoryName(package)!);
        using ZipArchive archive = ZipFile.Open(package, ZipArchiveMode.Create);
        using Stream entry = archive.CreateEntry(EngineHashes.FileName).Open();
        entry.Write(Encoding.UTF8.GetBytes($$"""{ "schema": "lockflight.engine-hashes/v1", "package": "Lockflight.Cli", "version": "{{Version}}", "commit": "{{new string('1', 40)}}", "binaries": { {{binaries}} } }"""));
    }
}
