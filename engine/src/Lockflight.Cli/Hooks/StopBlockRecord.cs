namespace Lockflight.Cli.Hooks;

/// <summary>
/// The session and inputs hash of the Stop gate's last block, in <c>.lockflight/local/stop-blocked</c> (git-ignored), cleared when
/// it next allows. The TaskCompleted hook reads it so it does not hold the agent a second time on a tree the Stop gate already
/// sent back in this session. A file that cannot be read or written only costs that courtesy.
/// </summary>
internal sealed class StopBlockRecord(string projectRoot)
{
    public const string RelativePath = ".lockflight/local/stop-blocked";

    private string FilePath => Path.Combine(projectRoot, RelativePath);

    public async Task RememberAsync(string? sessionId, string? inputsHash, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            await File.WriteAllTextAsync(FilePath, $"{sessionId}\n{inputsHash}\n", cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Without the record the TaskCompleted hook may block once more; the Stop gate's own decision stands.
        }
    }

    public void Clear()
    {
        try
        {
            File.Delete(FilePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A stale record only lets one task completion through to the Stop gate, which judges the turn again.
        }
    }

    /// <summary>Whether the Stop gate blocked in this session on exactly this tree; false for an unknown session or tree.</summary>
    public async Task<bool> BlockedAsync(string? sessionId, string? inputsHash, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(sessionId) || inputsHash is null)
        {
            return false;
        }

        try
        {
            string[] lines = File.Exists(FilePath) ? await File.ReadAllLinesAsync(FilePath, cancellationToken) : [];
            return lines.Length >= 2 && lines[0] == sessionId && lines[1] == inputsHash;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
