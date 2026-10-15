namespace Lockflight.Cli.Gates;

/// <summary>
/// Remembers the inputs hash of the last stop-gate PASS in <c>.lockflight/local/stop-pass</c> (git-ignored) — for a unit of a
/// workspace in <c>.lockflight/local/stop-pass-&lt;unit&gt;</c>, one entry per unit. Only passes are cached: a red gate
/// always runs again. Losing or failing to read the file only costs one extra gate run.
/// </summary>
internal sealed class StopGateCache(string projectRoot, string? unitPath)
{
    public const string RelativePath = ".lockflight/local/stop-pass";

    private string FilePath => Path.Combine(projectRoot, unitPath is null ? RelativePath : $"{RelativePath}-{unitPath.Replace('/', '-')}");

    public async Task<bool> PassedWithAsync(string inputsHash, CancellationToken cancellationToken)
    {
        try
        {
            return File.Exists(FilePath) && (await File.ReadAllTextAsync(FilePath, cancellationToken)).Trim() == inputsHash;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An unreadable cache (a directory in its place, no permission) is a miss: the gate runs.
            return false;
        }
    }

    public async Task RememberPassAsync(string inputsHash, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            await File.WriteAllTextAsync(FilePath, inputsHash, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A cache that cannot be written only means the next stop runs the gate again.
        }
    }
}
