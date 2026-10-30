using Lockflight.Cli.EngineLocks;

namespace Lockflight.Cli.Ci;

/// <summary>
/// The hash the evidence records for the engine that judged: the target branch's <c>.lockflight/engine.lock</c> entry for this platform,
/// read with <c>git show</c>, when the lock pins the running version: the lock's hash, which the CI install step checks the
/// executable against; this engine does not hash itself. Null without a lock (a base still pinned the 0.2 way), or when the lock
/// pins another version.
/// </summary>
internal static class JudgeEngineHash
{
    public static async Task<string?> ReadAsync(JudgePlan plan, string engineVersion, string runtimeIdentifier, CancellationToken cancellationToken) =>
        await JudgeGit.ShowFileAsync(plan.Inputs.Root, plan.Policy.Commit, EngineLock.RelativePath, cancellationToken) is string text
            && EngineLockReader.Read(text, allowDevelopment: false, engineVersion).Lock is EngineLock engineLock
            && engineLock.Version == engineVersion
            ? engineLock.HashFor(runtimeIdentifier)
            : null;
}
