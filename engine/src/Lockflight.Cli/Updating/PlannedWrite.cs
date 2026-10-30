using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Updating;

/// <summary>
/// A file <c>update</c> will write, computed before anything is written: its old text (null when it does not exist) and its new
/// text (null to delete the file). All are written together, each through a temporary file and a rename; when one fails, the ones already written are put
/// back.
/// </summary>
internal sealed record PlannedWrite(string RelativePath, string Path, string? OldText, string? NewText)
{
    public static async Task ApplyAllAsync(IReadOnlyList<PlannedWrite> writes, CancellationToken cancellationToken)
    {
        List<PlannedWrite> written = [];
        try
        {
            foreach (PlannedWrite write in writes)
            {
                if (write.NewText is string text)
                {
                    await ReplaceAsync(write.Path, text, cancellationToken);
                }
                else
                {
                    File.Delete(write.Path);
                }

                written.Add(write);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            foreach (PlannedWrite write in written)
            {
                if (write.OldText is string old)
                {
                    await ReplaceAsync(write.Path, old, CancellationToken.None);
                }
                else
                {
                    File.Delete(write.Path);
                }
            }

            throw;
        }
    }

    /// <summary>A unified diff of the change (<c>git diff --no-index</c>); the new text when git cannot make one.</summary>
    public async Task<string> DiffAsync(CancellationToken cancellationToken)
    {
        string directory = Directory.CreateTempSubdirectory("lockflight-update-").FullName;
        try
        {
            string before = System.IO.Path.Combine(directory, "before");
            string after = System.IO.Path.Combine(directory, "after");
            await File.WriteAllTextAsync(before, OldText ?? string.Empty, cancellationToken);
            await File.WriteAllTextAsync(after, NewText ?? string.Empty, cancellationToken);
            ProcessResult diff = await ProcessRunner.RunAsync("git", ["diff", "--no-index", "--no-color", "--no-ext-diff", "--", "before", "after"], directory, cancellationToken);
            return diff.ExitCode == 1 ? diff.Output.TrimEnd() : NewText ?? "(deleted)";
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task ReplaceAsync(string path, string text, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        string temporary = $"{path}.lockflight-{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temporary, text, cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }
}
