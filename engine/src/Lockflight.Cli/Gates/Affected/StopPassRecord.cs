using System.Security.Cryptography;
using System.Text;

namespace Lockflight.Cli.Gates.Affected;

/// <summary>
/// The working tree (<see cref="PassTree"/>) at a target's last stop PASS, with the engine and settings it passed under, in
/// <c>.lockflight/local/affected/&lt;target&gt;.pass</c>: what the next stop run compares with to choose projects. Written only
/// when a stop run of that target passed; a record under other settings, or none, makes the next run build everything.
/// Losing it costs one full run.
/// </summary>
internal sealed record StopPassRecord(string Location, string Settings, string Tree)
{
    /// <summary>Where the records and solution filters live: git-ignored local state.</summary>
    public const string RelativeDirectory = ".lockflight/local/affected";

    private const int NameLength = 16;

    /// <summary>A name for the target, the same for every run of it and distinct for every other target.</summary>
    public static string TargetName(string projectRoot, string target) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetRelativePath(projectRoot, target).Replace('\\', '/'))))[..NameLength];

    /// <summary>The settings a record is kept under: a fingerprint of <see cref="InputsHash.EngineAndSettings"/> and the resolved SDK.</summary>
    public static string SettingsOf(string projectRoot, string sdk) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{InputsHash.EngineAndSettings(projectRoot)}|sdk:{sdk}")));

    public static string PathOf(string projectRoot, string target, string extension) =>
        Path.Combine(projectRoot, RelativeDirectory.Replace('/', Path.DirectorySeparatorChar), TargetName(projectRoot, target) + extension);

    /// <summary>The tree of the last PASS recorded under these settings; null when there is none or it cannot be read.</summary>
    public static async Task<string?> PassedTreeAsync(string location, string settings, CancellationToken cancellationToken)
    {
        try
        {
            string[] lines = File.Exists(location) ? await File.ReadAllLinesAsync(location, cancellationToken) : [];
            return lines is [string recorded, string tree] && recorded == settings && tree.Length > 0 ? tree : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task RememberAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Location)!);
            await File.WriteAllLinesAsync(Location, [Settings, Tree], cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Without a record the next stop run builds everything.
        }
    }
}
