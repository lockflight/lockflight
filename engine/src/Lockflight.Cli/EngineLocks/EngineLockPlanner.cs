using System.Security.Cryptography;
using Lockflight.Cli.Config;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.EngineLocks;

/// <summary>
/// Works out the engine lock for the running engine. A release takes every platform's hash from the release's
/// <c>engine-hashes.json</c> in the pointer package on the feed, after checking that it describes this very engine (its own
/// platform's executable), that GitHub's attestation of the file verifies when the <c>gh</c> CLI is there, and that the GitHub
/// Release serves the same bytes when it can be reached, and that the release's install script is the one this engine vendors.
/// The first lock trusts the feed and the attestation; the person who commits it makes it policy (the GitHub action checks it
/// against the release; on the other platforms the reviewer runs <c>engine verify-lock</c>). A development build writes a git-ignored local lock
/// with its own hash only; a local build (<c>0.0.0-local</c>, the test builds) writes none.
/// </summary>
internal static class EngineLockPlanner
{
    public const string Repository = "lockflight/lockflight";
    private const string LocalBuild = "0.0.0-local";
    private static readonly TimeSpan AttestationTimeLimit = TimeSpan.FromMinutes(2);

    /// <param name="engineBinary">The running engine's executable, hashed to check the feed's file describes it.</param>
    /// <param name="releaseHashes">Fetches the GitHub Release's <c>engine-hashes.json</c>; null when it cannot be reached.</param>
    public static async Task<EngineLockPlan> PlanAsync(
        string root, string version, string? engineBinary, EnginePackageFeed feed, Func<string, CancellationToken, Task<byte[]?>> releaseHashes,
        RunProcess processes, CancellationToken cancellationToken)
    {
        if (version.StartsWith(LocalBuild, StringComparison.OrdinalIgnoreCase))
        {
            return EngineLockPlan.None($"a local build ({version}) pins nothing by hash");
        }

        if (EngineRids.Current is not string rid || engineBinary is null || !File.Exists(engineBinary))
        {
            return EngineLockPlan.None("this engine's own executable cannot be hashed on this platform");
        }

        string ownHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(engineBinary)));
        if (EngineVersion.IsDevelopmentBuild(version))
        {
            EngineLock development = new(version, EngineLockChannel.Development, InstallScript.Sha256, new Dictionary<string, string> { [rid] = ownHash });
            return new EngineLockPlan(EngineLockFiles.LocalPath(root), development, [$"development build: {EngineLock.LocalRelativePath} pins this engine for this clone only"], null);
        }

        (EngineHashes? hashes, EngineFetch fetch) = await feed.FetchHashesAsync(version, cancellationToken);
        if (hashes is null)
        {
            return EngineLockPlan.None($"no executable hashes for {version}: {fetch.Message}");
        }

        if (hashes.Binaries[rid].Sha256 != ownHash)
        {
            return EngineLockPlan.None($"the feed's {EngineHashes.FileName} for {version} does not describe this engine ({rid} is {ownHash}, the file says {hashes.Binaries[rid].Sha256}); nothing was pinned");
        }

        if (hashes.InstallScriptSha256 is string releasedScript && releasedScript != InstallScript.Sha256)
        {
            return EngineLockPlan.None($"the feed's {EngineHashes.FileName} for {version} names another install script ({releasedScript}) than this engine carries ({InstallScript.Sha256}); nothing was pinned");
        }

        List<string> notes = [$"{EngineHashes.FileName} of {version} from the feed describes this engine ({rid})"];
        byte[] content = fetch.Content!;
        byte[]? release = await releaseHashes(version, cancellationToken);
        if (release is null)
        {
            notes.Add($"warning: the GitHub Release v{version} could not be reached to compare its {EngineHashes.FileName}");
        }
        else if (!release.AsSpan().SequenceEqual(content))
        {
            return EngineLockPlan.None($"the feed's {EngineHashes.FileName} for {version} differs from the GitHub Release's: a mirror or proxy serves something else; nothing was pinned");
        }
        else
        {
            notes.Add($"the GitHub Release v{version} serves the same {EngineHashes.FileName}");
        }

        notes.Add(await AttestationAsync(content, version, processes, cancellationToken));
        EngineLock committed = new(version, EngineLockChannel.Release, InstallScript.Sha256, hashes.Binaries.ToDictionary(pair => pair.Key, pair => pair.Value.Sha256, StringComparer.Ordinal));
        return new EngineLockPlan(EngineLockFiles.CommittedPath(root), committed, notes, null) { Committed = true };
    }

    /// <summary>Verifies the release workflow's attestation of the file with <c>gh attestation verify</c>; a note either way.</summary>
    public static async Task<string> AttestationAsync(
        byte[] content, string version, RunProcess processes, CancellationToken cancellationToken)
    {
        if (Executables.Find("gh", explicitPath: null) is not string gh)
        {
            return "warning: the gh CLI is not installed, so the release's attestation was not verified (CI's check of the lock against the release does)";
        }

        string directory = Directory.CreateTempSubdirectory("lockflight-attestation-").FullName;
        try
        {
            string file = Path.Combine(directory, EngineHashes.FileName);
            await File.WriteAllBytesAsync(file, content, cancellationToken);
            using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(AttestationTimeLimit);
            ProcessResult result = await processes(gh, ["attestation", "verify", file, "--repo", Repository, "--source-ref", $"refs/tags/v{version}"], directory, limit.Token);
            return result.ExitCode == 0
                ? $"the release workflow's attestation of {EngineHashes.FileName} verifies (gh attestation verify)"
                : $"warning: gh attestation verify did not verify {EngineHashes.FileName}: {FirstLine(result.Error + result.Output)}";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return "warning: gh attestation verify did not answer in time";
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string FirstLine(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "no output";
}
