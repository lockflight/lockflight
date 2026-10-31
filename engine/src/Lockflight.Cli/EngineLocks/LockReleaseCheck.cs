using Lockflight.Cli.Config;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.EngineLocks;

/// <summary>The outcome of comparing a lock with its release: the verdict and a line a reviewer can read instead of 64-hex strings.</summary>
internal sealed record LockReleaseCheck(LockReleaseVerdict Verdict, string Detail)
{
    /// <summary>
    /// Compares every platform's hash and the install script's hash in the lock at <paramref name="lockText"/> with the
    /// <c>engine-hashes.json</c> the release of its version published on <paramref name="feed"/>. A release lock must name exactly
    /// the released platforms' hashes and the script the release shipped; a release that published no script hash cannot vouch for
    /// the script the lock pins, so the check fails.
    /// </summary>
    /// <param name="baseLockText">The lock the pull request replaces; a lower version than its own is named in the verdict.</param>
    /// <param name="attestation">Also verifies GitHub's attestation of the release's file with <c>gh</c>; null skips it.</param>
    public static async Task<LockReleaseCheck> RunAsync(string lockText, string? baseLockText, EnginePackageFeed feed, RunProcess? attestation, CancellationToken cancellationToken)
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

        if (hashes.InstallScriptSha256 is null)
        {
            return new LockReleaseCheck(LockReleaseVerdict.Differs, $"pins {engineLock.Version}, whose release published no install script hash, so the install script it pins cannot be checked");
        }

        if (engineLock.InstallScriptSha256 != hashes.InstallScriptSha256)
        {
            return new LockReleaseCheck(LockReleaseVerdict.Differs, $"pins {engineLock.Version}, but its install script hash is not the one release v{engineLock.Version} published: {InstallScript.RelativePath} is not the release's script");
        }

        string matches = $"matches the published hashes of release v{engineLock.Version} (commit {hashes.Commit[..12]}), its install script included{Downgrade(engineLock, baseLockText)}";
        if (attestation is null)
        {
            return new LockReleaseCheck(LockReleaseVerdict.Matches, matches);
        }

        string note = await EngineLockPlanner.AttestationAsync(fetch.Content!, engineLock.Version, attestation, cancellationToken);
        return note.StartsWith("warning:", StringComparison.Ordinal)
            ? new LockReleaseCheck(LockReleaseVerdict.Unattested, $"{matches}, but {note["warning: ".Length..]}")
            : new LockReleaseCheck(LockReleaseVerdict.Matches, $"{matches}; {note}");
    }

    /// <summary>
    /// A warning when the lock moves the engine to a lower version than the base's lock, or when the base's lock cannot be read so
    /// a move down cannot be told; empty otherwise.
    /// </summary>
    private static string Downgrade(EngineLock engineLock, string? baseLockText)
    {
        if (baseLockText is null)
        {
            return string.Empty;
        }

        EngineLockReading baseReading = EngineLockReader.Read(baseLockText, allowDevelopment: false, ToolPin.RunningVersion);
        if (baseReading.Lock is not EngineLock baseLock)
        {
            return $"; warning: the base's lock cannot be read ({baseReading.Problem}), so a move to an older engine is not checked";
        }

        if (EngineVersion.Parse(baseLock.Version) is not EngineVersion from || EngineVersion.Parse(engineLock.Version) is not EngineVersion to
            || to.CompareTo(from) >= 0)
        {
            return string.Empty;
        }

        return $"; warning: it moves the engine down from {baseLock.Version} to {engineLock.Version}, an older judge; accept that only on purpose";
    }
}
