namespace Lockflight.Cli.Paths;

/// <summary>
/// Deletes the engine's own temporary directories without failing the work they served. On Windows a just-exited executable's
/// image, and an antivirus scan of a freshly written one, are released a moment after the process ends, so an immediate delete of
/// the folder that holds it can fail with a sharing violation; the delete is retried for a short while, then the folder is left in
/// the temporary folder.
/// </summary>
internal static class ScratchDirectories
{
    private const int Attempts = 5;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>Deletes <paramref name="directory"/> and everything in it; false when it is still there after the retries.</summary>
    public static async Task<bool> TryDeleteAsync(DirectoryInfo directory, TimeProvider time, CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                directory.Refresh();
                if (directory.Exists)
                {
                    directory.Delete(recursive: true);
                }

                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt == Attempts)
                {
                    return false;
                }
            }

            try
            {
                await Task.Delay(RetryDelay, time, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
    }
}
