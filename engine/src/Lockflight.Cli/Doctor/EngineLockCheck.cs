using System.Security.Cryptography;
using Lockflight.Cli.Config;
using Lockflight.Cli.EngineLocks;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Linking;

namespace Lockflight.Cli.Doctor;

/// <summary>
/// What <c>doctor</c> says about the engine lock: whether it can be read, whether the linked engine is the one it pins for this
/// platform, whether <c>dotnet-tools.json</c> still mirrors its version (kept so 0.2 tooling keeps working during 0.3.x), and
/// whether the vendored install script is the one it pins.
/// </summary>
internal static class EngineLockCheck
{
    private const string Name = "engine lock";

    public static IReadOnlyList<DoctorCheck> Check(string root)
    {
        EngineLockReading reading = EngineLockFiles.ReadForClone(root);
        if (reading.Problem is string problem)
        {
            return [new(Name, CheckStatus.Fail, problem, $"fix the lock (a protected file: a person's edit), or {EngineCommand.UserInstall} update")];
        }

        if (reading.Lock is not EngineLock engineLock)
        {
            return ToolPin.ReadPin(root) is { Status: ToolPinStatus.Pinned, Version: string pinned }
                ? [new(Name, CheckStatus.Warn, $"none; the engine is pinned by version only ({pinned} in dotnet-tools.json)",
                    $"a person runs {EngineCommand.UserInstall} update: it writes {EngineLock.RelativePath} with each platform's hash")]
                : [];
        }

        List<DoctorCheck> checks = [Linked(root, engineLock)];
        if (engineLock.Channel == EngineLockChannel.Release)
        {
            checks.Add(Mirror(root, engineLock));
            checks.Add(Script(root, engineLock));
        }

        return checks;
    }

    private static DoctorCheck Linked(string root, EngineLock engineLock)
    {
        string? locked = EngineRids.Current is string rid ? engineLock.HashFor(rid) : null;
        string? recorded = EngineRecord.RecordedHash(root);
        string install = $"a person runs {EngineCommand.UserInstall} engine install (it installs and links exactly the locked engine)";
        return (locked, recorded) switch
        {
            (null, _) => new(Name, CheckStatus.Fail, $"{engineLock.Version} names no engine for this platform", null),
            (_, null) => new(Name, CheckStatus.Fail, $"pins {engineLock.Version}; no engine is linked", install),
            _ when locked == recorded => new(Name, CheckStatus.Pass, $"pins {engineLock.Version} ({EngineLockNames.ToName(engineLock.Channel)}); the linked engine is that one", null),
            _ => new(Name, CheckStatus.Fail, $"pins {engineLock.Version}, but the linked engine is another one", install),
        };
    }

    private static DoctorCheck Mirror(string root, EngineLock engineLock) => ToolPin.ReadPin(root) switch
    {
        { Status: ToolPinStatus.Pinned, Version: string pinned } when ToolPin.Matches(pinned, engineLock.Version) =>
            new($"{Name} mirror", CheckStatus.Pass, $"dotnet-tools.json pins {pinned} too", null),
        { Status: ToolPinStatus.Pinned, Version: string pinned } =>
            new($"{Name} mirror", CheckStatus.Fail, $"dotnet-tools.json pins {pinned}, {EngineLock.RelativePath} {engineLock.Version}; they must agree",
                $"a person runs {EngineCommand.UserInstall} update, or sets the same version in both"),
        _ => new($"{Name} mirror", CheckStatus.Warn, $"no lockflight.cli entry in dotnet-tools.json: tools of Lockflight 0.2 cannot restore the engine",
            $"a person runs {EngineCommand.UserInstall} update, which keeps the entry as a mirror of the lock"),
    };

    private static DoctorCheck Script(string root, EngineLock engineLock)
    {
        string path = Path.Combine(root, InstallScript.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            return new("install script", CheckStatus.Fail, $"{InstallScript.RelativePath} is missing; CI checks it against the lock before running it",
                $"a person runs {EngineCommand.UserInstall} update");
        }

        string hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        if (hash != engineLock.InstallScriptSha256)
        {
            return new("install script", CheckStatus.Fail, $"{InstallScript.RelativePath} is not the script {EngineLock.RelativePath} pins (CI would refuse it)",
                $"a person runs {EngineCommand.UserInstall} update");
        }

        return hash == InstallScript.Sha256
            ? new("install script", CheckStatus.Pass, $"{InstallScript.RelativePath} matches the lock", null)
            : new("install script", CheckStatus.Warn, $"{InstallScript.RelativePath} matches the lock but is another engine's copy", $"{EngineCommand.UserInstall} update writes this engine's");
    }
}
