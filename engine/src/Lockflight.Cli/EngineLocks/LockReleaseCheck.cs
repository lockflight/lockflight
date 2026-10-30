using Lockflight.Cli.Config;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.EngineLocks;

/// <summary>The outcome of comparing a lock with its release: the verdict and a line a reviewer can read instead of 64-hex strings.</summary>
internal sealed record LockReleaseCheck(LockReleaseVerdict Verdict, string Detail)
{
    /// <summary>
    /// Compares every platform's hash in the lock at <paramref name="lockText"/> with the <c>engine-hashes.json</c> the release of
    /// its version published on <paramref name="feed"/>. A release lock must name exactly the released platforms' hashes.
    /// </summary>
    /// <param name="attestation">Also verifies GitHub's attestation of the release's file with <c>gh</c>; null skips it.</param>
    public static async Task<LockReleaseCheck> RunAsync(string lockText, EnginePackageFeed feed, RunProcess? attestation, CancellationToken cancellationToken)
    {
        EngineLockReading reading = EngineLockReader.Read(lockText, allowDevelopment: false, ToolPin.RunningVersion);
        if (reading.Lock is not EngineLock engineLock)
        {
            return new LockReleaseCheck(LockReleaseVerdict.Unreadable, $"the lock cannot be read: {reading.Problem}");
        }

        (EngineHashes? hashes, EngineFetch fetch) = await feed.FetchHashesAsync(engineLock.Version, cancellationToken);
        if (hashes is null)
        {
            return new LockReleaseCheck(LockReleaseVerdict.NoRelease, $"no published hashes for {engineLock.Version}: {fetch.Message}");
        }

        string[] differing = [.. EngineRids.Released.Where(rid => engineLock.HashFor(rid) != hashes.Binaries[rid].Sha256)];
        if (differing.Length > 0)
        {
            return new LockReleaseCheck(LockReleaseVerdict.Differs, $"pins {engineLock.Version}, but its hashes for {string.Join(", ", differing)} are not the ones release v{engineLock.Version} published");
        }

        string matches = $"matches the published hashes of release v{engineLock.Version} (commit {hashes.Commit[..12]})";
        if (attestation is null)
        {
            return new LockReleaseCheck(LockReleaseVerdict.Matches, matches);
        }

        string note = await EngineLockPlanner.AttestationAsync(fetch.Content!, engineLock.Version, attestation, cancellationToken);
        return note.StartsWith("warning:", StringComparison.Ordinal)
            ? new LockReleaseCheck(LockReleaseVerdict.Unattested, $"{matches}, but {note["warning: ".Length..]}")
            : new LockReleaseCheck(LockReleaseVerdict.Matches, $"{matches}; {note}");
    }
}
