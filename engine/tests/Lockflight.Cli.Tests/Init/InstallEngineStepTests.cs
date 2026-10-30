using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Lockflight.Cli.EngineLocks;
using Lockflight.Cli.Init;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Init;

/// <summary>
/// The CI step that installs the engine, run as a pipeline runs it (<c>sh -e</c>) in a scratch checkout: from the engine lock
/// through the vendored install script, refusing a script or a pin mirror the lock does not name; without a lock, the version
/// pin through <c>dotnet tool install</c> (a stand-in records the call), as the base of the pull request that moves to the lock.
/// </summary>
public sealed class InstallEngineStepTests : IDisposable
{
    private const string Version = "0.0.0-ci.7";
    private static readonly byte[] Engine = Encoding.UTF8.GetBytes("#!/bin/sh\necho engine\n");

    private readonly DirectoryInfo repository = Directory.CreateTempSubdirectory("lockflight-ci-step-");
    private readonly DirectoryInfo temp = Directory.CreateTempSubdirectory("lockflight-ci-temp-");

    [Fact]
    public async Task TheLockedEngineIsInstalledByTheVendoredScript()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows() || EngineRids.Current is null, "POSIX pipelines only.");
        string feed = Lock(scriptBytes: InstallScript.Bytes, pinned: Version);

        ProcessResult run = await RunStepAsync(feed);

        Assert.True(run.ExitCode == 0, run.Error + run.Output);
        Assert.Equal(Engine, File.ReadAllBytes(Path.Combine(temp.FullName, "lockflight", "lockflight")));
    }

    [Fact]
    public async Task AVendoredScriptTheLockDoesNotNameIsRefused()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows() || EngineRids.Current is null, "POSIX pipelines only.");
        string feed = Lock(scriptBytes: [.. InstallScript.Bytes, .. "\necho tampered\n"u8.ToArray()], pinned: Version);

        ProcessResult run = await RunStepAsync(feed);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("is not the script .lockflight/engine.lock pins", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APinMirrorThatDisagreesWithTheLockIsRefused()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows() || EngineRids.Current is null, "POSIX pipelines only.");
        string feed = Lock(scriptBytes: InstallScript.Bytes, pinned: "0.0.0-ci.6");

        ProcessResult run = await RunStepAsync(feed);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("they must agree", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutALockThePinnedVersionIsInstalledWithDotnet()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "POSIX pipelines only.");
        WritePin("0.2.0");
        string bin = Directory.CreateDirectory(Path.Combine(temp.FullName, "bin")).FullName;
        File.WriteAllText(Path.Combine(bin, "dotnet"), $"#!/bin/sh\nprintf '%s\\n' \"$*\" > \"{temp.FullName}/dotnet-args\"\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(Path.Combine(bin, "dotnet"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        ProcessResult run = await RunStepAsync(feed: null, pathPrefix: bin);

        Assert.True(run.ExitCode == 0, run.Error);
        Assert.Contains("tool install Lockflight.Cli --tool-path", File.ReadAllText(Path.Combine(temp.FullName, "dotnet-args")), StringComparison.Ordinal);
        Assert.Contains("--version 0.2.0", File.ReadAllText(Path.Combine(temp.FullName, "dotnet-args")), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        TempDirectories.DeleteRecursively(repository);
        TempDirectories.DeleteRecursively(temp);
    }

    private string Lock(byte[] scriptBytes, string pinned)
    {
        string rid = EngineRids.Current!;
        Dictionary<string, string> binaries = EngineRids.Released.ToDictionary(
            entry => entry, entry => entry == rid ? Convert.ToHexStringLower(SHA256.HashData(Engine)) : new string('9', 64), StringComparer.Ordinal);
        EngineLockFiles.Write(EngineLockFiles.CommittedPath(repository.FullName), new EngineLock(Version, EngineLockChannel.Release, InstallScript.Sha256, binaries));
        string script = Path.Combine(repository.FullName, InstallScript.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(script)!);
        File.WriteAllBytes(script, scriptBytes);
        WritePin(pinned);

        string id = $"lockflight.cli.{rid}";
        string package = Path.Combine(temp.FullName, "feed", id, Version, $"{id}.{Version}.nupkg");
        Directory.CreateDirectory(Path.GetDirectoryName(package)!);
        using (ZipArchive archive = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            using Stream entry = archive.CreateEntry($"tools/any/{rid}/{EngineRids.ExecutableName(rid)}").Open();
            entry.Write(Engine);
        }

        return new Uri(Path.Combine(temp.FullName, "feed") + "/").AbsoluteUri;
    }

    private void WritePin(string version) =>
        File.WriteAllText(Path.Combine(repository.FullName, "dotnet-tools.json"), "{\"version\":1,\"isRoot\":true,\"tools\":{\"lockflight.cli\":{\"version\":\"" + version + "\",\"commands\":[\"lockflight\"]}}}");

    private async Task<ProcessResult> RunStepAsync(string? feed, string? pathPrefix = null)
    {
        string step = Path.Combine(temp.FullName, "step.sh");
        File.WriteAllText(step, CiShellScripts.InstallEngine + "\n");
        List<string> arguments = [$"LOCKFLIGHT_TEMP={temp.FullName}", $"PATH={(pathPrefix is null ? string.Empty : pathPrefix + ":")}{Environment.GetEnvironmentVariable("PATH")}"];
        if (feed is not null)
        {
            arguments.Add($"LOCKFLIGHT_NUGET_FEED={feed}");
        }

        return await ProcessRunner.RunAsync("env", [.. arguments, "sh", "-e", step], repository.FullName, TestContext.Current.CancellationToken);
    }
}
