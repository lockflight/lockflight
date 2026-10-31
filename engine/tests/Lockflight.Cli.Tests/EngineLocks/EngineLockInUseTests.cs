using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Doctor;
using Lockflight.Cli.EngineLocks;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Linking;
using Lockflight.Cli.Updating;

namespace Lockflight.Cli.Tests.EngineLocks;

/// <summary>The engine lock where it is used: link, link --installed, engine install, the guard's engine rule and doctor.</summary>
public sealed class EngineLockInUseTests : IDisposable
{
    private const string Version = "0.0.0-ci.7";
    private static readonly byte[] Engine = Encoding.UTF8.GetBytes("locked engine");
    private static readonly string EngineHash = Convert.ToHexStringLower(SHA256.HashData(Engine));

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-lock-project-");
    private readonly DirectoryInfo home = Directory.CreateTempSubdirectory("lockflight-lock-home-");

    public EngineLockInUseTests() => File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 2\n");

    private static string Rid => EngineRids.Current!;

    [Fact]
    public async Task LinkRefusesAnEngineTheLockDoesNotPin()
    {
        Assert.SkipWhen(EngineRids.Current is null, "No released engine for this platform.");
        WriteLock(new string('a', 64));

        (ExitCode exitCode, _, string error) = await RunAsync(["link"], EngineFile("another engine"));

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("is not the one .lockflight/engine.lock pins (0.0.0-ci.7)", error, StringComparison.Ordinal);
        Assert.Contains("engine install", error, StringComparison.Ordinal);
        Assert.False(File.Exists(LinkCommand.BinaryPath(root.FullName)));
    }

    [Fact]
    public async Task LinkLinksTheEngineTheLockPins()
    {
        Assert.SkipWhen(EngineRids.Current is null, "No released engine for this platform.");
        WriteLock(EngineHash);

        (ExitCode exitCode, _, string error) = await RunAsync(["link"], EngineFile(Engine));

        Assert.True(exitCode == ExitCode.Pass, error);
        Assert.Equal($"{EngineHash} {Rid}\n", File.ReadAllText(EngineRecord.PathOf(root.FullName)));
    }

    [Fact]
    public async Task LinkInstalledLinksTheLockedEngineFromTheUsersTools()
    {
        Assert.SkipWhen(EngineRids.Current is null, "No released engine for this platform.");
        WriteLock(EngineHash);
        string installed = Path.Combine(UserInstall.ToolPath(home.FullName, Version), EngineRids.ExecutableName(Rid));
        Directory.CreateDirectory(Path.GetDirectoryName(installed)!);
        File.WriteAllBytes(installed, Engine);

        (ExitCode exitCode, string output, string error) = await RunAsync(["link", "--installed"], EngineFile("the running engine is another one"));

        Assert.True(exitCode == ExitCode.Pass, error);
        Assert.Contains("linked the installed lockflight 0.0.0-ci.7", output, StringComparison.Ordinal);
        Assert.Equal(Engine, File.ReadAllBytes(LinkCommand.BinaryPath(root.FullName)));
    }

    [Fact]
    public async Task LinkInstalledRefusesAnInstalledEngineWithAnotherHash()
    {
        Assert.SkipWhen(EngineRids.Current is null, "No released engine for this platform.");
        WriteLock(EngineHash);
        string installed = Path.Combine(UserInstall.ToolPath(home.FullName, Version), EngineRids.ExecutableName(Rid));
        Directory.CreateDirectory(Path.GetDirectoryName(installed)!);
        File.WriteAllText(installed, "tampered");

        (ExitCode exitCode, _, string error) = await RunAsync(["link", "--installed"], EngineFile("x"));

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains("with the locked hash is not installed", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LinkInstalledRefusesWhileALocalDevelopmentLockExists()
    {
        Assert.SkipWhen(EngineRids.Current is null, "No released engine for this platform.");
        WriteLock(new string('a', 64));
        string fake = Path.Combine(UserInstall.ToolPath(home.FullName, "9.9.9"), EngineRids.ExecutableName(Rid));
        Directory.CreateDirectory(Path.GetDirectoryName(fake)!);
        File.WriteAllBytes(fake, Engine);
        EngineLockFiles.Write(EngineLockFiles.LocalPath(root.FullName), new EngineLock("9.9.9", EngineLockChannel.Development, new string('5', 64), new Dictionary<string, string> { [Rid] = EngineHash }));

        (ExitCode exitCode, _, string error) = await RunAsync(["link", "--installed"], EngineFile("x"));

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("links only the engine the committed .lockflight/engine.lock pins", error, StringComparison.Ordinal);
        Assert.False(File.Exists(LinkCommand.BinaryPath(root.FullName)));
    }

    [Fact]
    public async Task EngineInstallInstallsAndLinksExactlyTheLockedEngine()
    {
        Assert.SkipWhen(EngineRids.Current is null, "No released engine for this platform.");
        WriteLock(EngineHash);
        string feed = WriteFeed(Engine);

        (ExitCode exitCode, string output, string error) = await RunAsync(["engine", "install", "--source", feed], EngineFile("x"));

        Assert.True(exitCode == ExitCode.Pass, error);
        Assert.Contains($"sha256 {EngineHash}", output, StringComparison.Ordinal);
        Assert.Equal(Engine, File.ReadAllBytes(Path.Combine(UserInstall.ToolPath(home.FullName, Version), EngineRids.ExecutableName(Rid))));
        Assert.Equal($"{EngineHash} {Rid}\n", File.ReadAllText(EngineRecord.PathOf(root.FullName)));
    }

    [Fact]
    public async Task EngineInstallRefusesAFeedWhoseEngineIsNotTheLockedOne()
    {
        Assert.SkipWhen(EngineRids.Current is null, "No released engine for this platform.");
        WriteLock(EngineHash);
        string feed = WriteFeed(Encoding.UTF8.GetBytes("a feed serving something else"));

        (ExitCode exitCode, _, string error) = await RunAsync(["engine", "install", "--source", feed], EngineFile("x"));

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains("nothing was installed", error, StringComparison.Ordinal);
        Assert.False(File.Exists(LinkCommand.BinaryPath(root.FullName)));
    }

    [Fact]
    public async Task EngineInstallIsAPersonsCommand()
    {
        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["engine", "install"], "", root.FullName, new Dictionary<string, string> { [EnvironmentVariables.ClaudeCode] = "1" });

        Assert.Equal(ExitCode.HookBlock, exitCode);
    }

    [Fact]
    public async Task TheGuardBlocksAWriteWhenTheLinkedEngineIsNotTheLockedOneAndAllowsTheRecovery()
    {
        Assert.SkipWhen(EngineRids.Current is null, "No released engine for this platform.");
        WriteLock(EngineHash);
        Directory.CreateDirectory(Path.Combine(root.FullName, ".lockflight", "bin"));
        EngineRecord.Write(root.FullName, new string('c', 64), Rid);

        (ExitCode write, _, string error) = await TestConsole.RunAsync(["hook", "pre-tool-use"], Payload("Write", "file_path", Path.Combine(root.FullName, "a.txt")), root.FullName);
        (ExitCode recovery, _, _) = await TestConsole.RunAsync(["hook", "pre-tool-use"], Payload("Bash", "command", "~/.lockflight/bin/lockflight link --installed"), root.FullName);

        Assert.Equal(ExitCode.HookBlock, write);
        Assert.Contains("is not the one .lockflight/engine.lock pins", error, StringComparison.Ordinal);
        Assert.Equal(ExitCode.Pass, recovery);
    }

    [Fact]
    public async Task TheGuardAllowsAWriteWhenTheLinkedEngineIsTheLockedOne()
    {
        Assert.SkipWhen(EngineRids.Current is null, "No released engine for this platform.");
        WriteLock(EngineHash);
        Directory.CreateDirectory(Path.Combine(root.FullName, ".lockflight", "bin"));
        EngineRecord.Write(root.FullName, EngineHash, Rid);

        (ExitCode write, _, _) = await TestConsole.RunAsync(["hook", "pre-tool-use"], Payload("Write", "file_path", Path.Combine(root.FullName, "a.txt")), root.FullName);

        Assert.Equal(ExitCode.Pass, write);
    }

    [Fact]
    public async Task AnUnreadableLockBlocksAWrite()
    {
        Directory.CreateDirectory(Path.Combine(root.FullName, ".lockflight"));
        File.WriteAllText(EngineLockFiles.CommittedPath(root.FullName), "{ not json");

        (ExitCode write, _, string error) = await TestConsole.RunAsync(["hook", "pre-tool-use"], Payload("Write", "file_path", Path.Combine(root.FullName, "a.txt")), root.FullName);

        Assert.Equal(ExitCode.HookBlock, write);
        Assert.Contains("the engine lock cannot be read", error, StringComparison.Ordinal);
    }

    [Fact]
    public void DoctorNamesEngineInstallWhenTheLinkedEngineIsNotTheLockedOne()
    {
        Assert.SkipWhen(EngineRids.Current is null, "No released engine for this platform.");
        WriteLock(EngineHash);
        Directory.CreateDirectory(Path.Combine(root.FullName, ".lockflight", "bin"));
        EngineRecord.Write(root.FullName, new string('c', 64), Rid);

        DoctorCheck linked = EngineLockCheck.Check(root.FullName)[0];

        Assert.Equal(CheckStatus.Fail, linked.Status);
        Assert.Contains("engine install", linked.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void DoctorChecksThePinMirrorAndTheVendoredScript()
    {
        Assert.SkipWhen(EngineRids.Current is null, "No released engine for this platform.");
        WriteLock(EngineHash, InstallScript.Sha256);
        File.WriteAllText(Path.Combine(root.FullName, "dotnet-tools.json"), """{"version":1,"isRoot":true,"tools":{"lockflight.cli":{"version":"0.0.0-ci.6","commands":["lockflight"]}}}""");

        IReadOnlyList<DoctorCheck> checks = EngineLockCheck.Check(root.FullName);
        Assert.Equal(CheckStatus.Fail, checks.Single(check => check.Name == "engine lock mirror").Status);
        Assert.Contains("is missing", checks.Single(check => check.Name == "install script").Detail, StringComparison.Ordinal);

        string script = Path.Combine(root.FullName, InstallScript.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(script)!);
        File.WriteAllBytes(script, InstallScript.Bytes);
        Assert.Equal(CheckStatus.Pass, EngineLockCheck.Check(root.FullName).Single(check => check.Name == "install script").Status);
    }

    [Fact]
    public void ThePinnedProjectWithoutALockIsToldUpdateWritesOne()
    {
        File.WriteAllText(Path.Combine(root.FullName, "dotnet-tools.json"), """{"version":1,"isRoot":true,"tools":{"lockflight.cli":{"version":"0.2.0","commands":["lockflight"]}}}""");

        DoctorCheck check = Assert.Single(EngineLockCheck.Check(root.FullName));

        Assert.Equal(CheckStatus.Warn, check.Status);
    }

    public void Dispose()
    {
        TempDirectories.DeleteRecursively(root);
        TempDirectories.DeleteRecursively(home);
    }

    private static string Payload(string tool, string key, string value) =>
        JsonSerializer.Serialize(new Dictionary<string, object> { ["tool_name"] = tool, ["tool_input"] = new Dictionary<string, string> { [key] = value } });

    private void WriteLock(string hash, string? scriptHash = null)
    {
        Dictionary<string, string> binaries = EngineRids.Released.ToDictionary(rid => rid, rid => rid == EngineRids.Current ? hash : new string('9', 64), StringComparer.Ordinal);
        EngineLockFiles.Write(EngineLockFiles.CommittedPath(root.FullName), new EngineLock(Version, EngineLockChannel.Release, scriptHash ?? new string('5', 64), binaries));
    }

    private string WriteFeed(byte[] engine)
    {
        string id = $"lockflight.cli.{Rid}";
        string package = Path.Combine(home.FullName, "feed", id, Version, $"{id}.{Version}.nupkg");
        Directory.CreateDirectory(Path.GetDirectoryName(package)!);
        using (ZipArchive archive = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            using Stream entry = archive.CreateEntry($"tools/any/{Rid}/{EngineRids.ExecutableName(Rid)}").Open();
            entry.Write(engine);
        }

        return Path.Combine(home.FullName, "feed");
    }

    private string EngineFile(string content) => EngineFile(Encoding.UTF8.GetBytes(content));

    private string EngineFile(byte[] content)
    {
        string path = Path.Combine(home.FullName, Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(path, content);
        return path;
    }

    private async Task<(ExitCode ExitCode, string Output, string Error)> RunAsync(string[] arguments, string engine)
    {
        using StringWriter output = new();
        using StringWriter error = new();
        CliContext context = new(new StringReader(string.Empty), output, error, root.FullName, TimeProvider.System)
        {
            Environment = EnvironmentReader.Of(new Dictionary<string, string> { [EnvironmentVariables.Home] = home.FullName }),
            Engine = new RunningEngine(engine, IsSingleFile: true),
        };

        ExitCode exitCode = await CliApplication.RunAsync(arguments, context, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString().Trim(), error.ToString().Trim());
    }
}
