using System.Runtime.InteropServices;

namespace Lockflight.Cli.EngineLocks;

/// <summary>
/// The platforms a release ships a Native AOT engine for, by runtime identifier, and the one this process runs on. The plugin's
/// shim takes the RID from the link record instead of detecting it, so the two never disagree (Rosetta reports x64).
/// </summary>
internal static class EngineRids
{
    /// <summary>Every released RID, in the order the lock and the release list them.</summary>
    public static IReadOnlyList<string> Released { get; } = ["linux-arm64", "linux-x64", "osx-arm64", "osx-x64", "win-x64"];

    /// <summary>The RID this engine was built for; null on a platform no release ships.</summary>
    public static string? Current => (OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), OperatingSystem.IsLinux(), RuntimeInformation.ProcessArchitecture) switch
    {
        (true, _, _, Architecture.X64) => "win-x64",
        (_, true, _, Architecture.Arm64) => "osx-arm64",
        (_, true, _, Architecture.X64) => "osx-x64",
        (_, _, true, Architecture.X64) => "linux-x64",
        (_, _, true, Architecture.Arm64) => "linux-arm64",
        _ => null,
    };

    /// <summary>The executable's name inside a RID package: <c>lockflight.exe</c> on Windows, else <c>lockflight</c>.</summary>
    public static string ExecutableName(string rid) => rid.StartsWith("win-", StringComparison.Ordinal) ? "lockflight.exe" : "lockflight";
}
