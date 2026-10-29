using System.Net;

namespace Lockflight.Cli.EngineLocks;

/// <summary>
/// The <c>engine-hashes.json</c> attached to the GitHub Release of a version, to compare with the feed's copy. Not an independent
/// source (the same release run produces both): it catches a corporate mirror or proxy that serves something else.
/// </summary>
internal static class GitHubReleaseHashes
{
    private static readonly TimeSpan TimeLimit = TimeSpan.FromSeconds(30);

    /// <summary>The file's bytes; null when the Release cannot be reached or has no such file.</summary>
    public static async Task<byte[]?> FetchAsync(string version, CancellationToken cancellationToken)
    {
        using HttpClient client = new() { Timeout = TimeLimit };
        try
        {
            using HttpResponseMessage response = await client.GetAsync(
                $"https://github.com/{EngineLockPlanner.Repository}/releases/download/v{version}/{EngineHashes.FileName}", cancellationToken);
            return response.StatusCode == HttpStatusCode.OK ? await response.Content.ReadAsByteArrayAsync(cancellationToken) : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
