using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Lockflight.Cli.EngineLocks;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Tests.Hooks;

namespace Lockflight.Cli.Tests.EngineLocks;

public sealed class EngineLockPlannerTests : IDisposable
{
    private const string Version = "0.0.0-ci.7";
    private static readonly byte[] Engine = Encoding.UTF8.GetBytes("this engine");
    private static readonly string EngineHash = Convert.ToHexStringLower(SHA256.HashData(Engine));
    private static readonly RunProcess NoGh = (_, _, _, _) => Task.FromResult(new ProcessResult(1, string.Empty, "gh: not logged in", 0, TimedOut: false));

    private readonly DirectoryInfo work = Directory.CreateTempSubdirectory("lockflight-planner-");

    private string EngineBinary => Path.Combine(work.FullName, "engine");

    public EngineLockPlannerTests() => File.WriteAllBytes(EngineBinary, Engine);

    [Fact]
    public async Task AReleaseLockTakesEveryPlatformsHashFromTheReleaseAfterCheckingItDescribesThisEngine()
    {
        Assert.SkipWhen(EngineRids.Current is null, "No released engine for this platform.");
        byte[] hashes = WritePointer(EngineHash);

        EngineLockPlan plan = await PlanAsync(Version, release: hashes);

        Assert.True(plan.Committed);
        Assert.Equal((Version, EngineLockChannel.Release, InstallScript.Sha256), (plan.Lock!.Version, plan.Lock.Channel, plan.Lock.InstallScriptSha256));
        Assert.Equal(EngineHash, plan.Lock.HashFor(EngineRids.Current!));
        Assert.Contains(plan.Notes, note => note.Contains("serves the same", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HashesThatDoNotDescribeThisEnginePinNothing()
    {
        Assert.SkipWhen(EngineRids.Current is null, "No released engine for this platform.");
        WritePointer(new string('a', 64));

        EngineLockPlan plan = await PlanAsync(Version, release: null);

        Assert.Null(plan.Lock);
        Assert.Contains("does not describe this engine", plan.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReleaseThatServesOtherHashesThanTheFeedPinsNothing()
    {
        Assert.SkipWhen(EngineRids.Current is null, "No released engine for this platform.");
        WritePointer(EngineHash);

        EngineLockPlan plan = await PlanAsync(Version, release: "{}"u8.ToArray());

        Assert.Null(plan.Lock);
        Assert.Contains("differs from the GitHub Release's", plan.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReleaseThatNamesAnotherInstallScriptPinsNothing()
    {
        Assert.SkipWhen(EngineRids.Current is null, "No released engine for this platform.");
        byte[] hashes = WritePointer(EngineHash, script: new string('e', 64));

        EngineLockPlan plan = await PlanAsync(Version, release: hashes);

        Assert.Null(plan.Lock);
        Assert.Contains("names another install script", plan.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReleaseThatNamesThisEnginesInstallScriptPinsIt()
    {
        Assert.SkipWhen(EngineRids.Current is null, "No released engine for this platform.");
        byte[] hashes = WritePointer(EngineHash, script: InstallScript.Sha256);

        EngineLockPlan plan = await PlanAsync(Version, release: hashes);

        Assert.Equal(InstallScript.Sha256, plan.Lock!.InstallScriptSha256);
    }

    [Fact]
    public async Task AnUnreachableReleaseIsAWarningNotARefusal()
    {
        Assert.SkipWhen(EngineRids.Current is null, "No released engine for this platform.");
        WritePointer(EngineHash);

        EngineLockPlan plan = await PlanAsync(Version, release: null);

        Assert.NotNull(plan.Lock);
        Assert.Contains(plan.Notes, note => note.StartsWith("warning: the GitHub Release", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AVersionWithoutPublishedHashesPinsNothing()
    {
        EngineLockPlan plan = await PlanAsync(Version, release: null);

        Assert.Null(plan.Lock);
        Assert.Contains("no executable hashes", plan.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADevelopmentBuildPinsItselfInTheLocalLockOnly()
    {
        Assert.SkipWhen(EngineRids.Current is null, "No released engine for this platform.");

        EngineLockPlan plan = await PlanAsync("0.3.1-0.dev.20261101120000", release: null);

        Assert.False(plan.Committed);
        Assert.Equal(EngineLockFiles.LocalPath(work.FullName), plan.LockPath);
        Assert.Equal((EngineLockChannel.Development, EngineHash), (plan.Lock!.Channel, plan.Lock.HashFor(EngineRids.Current!)));
        Assert.Single(plan.Lock.Binaries);
    }

    [Fact]
    public async Task ALocalBuildPinsNothing() =>
        Assert.Null((await PlanAsync("0.0.0-local", release: null)).Lock);

    [Fact]
    public void TheEngineCarriesTheInstallScriptOfThisCommit() =>
        Assert.Equal(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(ShimLocation.Path)!, "..", "..", "..", "scripts", "install.sh")), InstallScript.Bytes);

    public void Dispose() => TempDirectories.DeleteRecursively(work);

    private Task<EngineLockPlan> PlanAsync(string version, byte[]? release) =>
        EngineLockPlanner.PlanAsync(
            work.FullName, version, EngineBinary, new EnginePackageFeed(Path.Combine(work.FullName, "feed"), EnvironmentReader.Of(new Dictionary<string, string>())),
            (_, _) => Task.FromResult(release), NoGh, TestContext.Current.CancellationToken);

    private byte[] WritePointer(string ownHash, string? script = null)
    {
        string scriptEntry = script is null ? string.Empty : $$""", "installScript": { "sha256": "{{script}}" }""";
        string binaries = string.Join(",\n", EngineRids.Released.Select(rid =>
            $"    \"{rid}\": {{ \"sha256\": \"{(rid == EngineRids.Current ? ownHash : new string('9', 64))}\", \"path\": \"tools/any/{rid}/{EngineRids.ExecutableName(rid)}\" }}"));
        byte[] hashes = Encoding.UTF8.GetBytes($$"""
            {
              "schema": "lockflight.engine-hashes/v1",
              "package": "Lockflight.Cli",
              "version": "{{Version}}",
              "commit": "{{new string('1', 40)}}",
              "binaries": {
            {{binaries}}
              }{{scriptEntry}}
            }

            """);
        string package = Path.Combine(work.FullName, "feed", "lockflight.cli", Version, $"lockflight.cli.{Version}.nupkg");
        Directory.CreateDirectory(Path.GetDirectoryName(package)!);
        using (ZipArchive archive = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            using Stream entry = archive.CreateEntry(EngineHashes.FileName).Open();
            entry.Write(hashes);
        }

        return hashes;
    }
}
