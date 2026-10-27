using Lockflight.Cli.Lifecycle.Approvals;

namespace Lockflight.Cli.Tests;

/// <summary>
/// Saves an approval over the artifacts as they are now: a shortcut for setting up tests only. The engine always saves the
/// hashes the person was shown, never re-reads the files at save time.
/// </summary>
internal static class ApprovalStoreTestExtensions
{
    public static string Save(this ApprovalStore store, ApprovalPhase phase, string approvedBy, DateTimeOffset at, string engineVersion) =>
        store.Save(phase, [.. store.CurrentHashes(phase)], approvedBy, at, engineVersion);
}
