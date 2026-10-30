namespace Lockflight.Cli.EngineLocks;

/// <summary>
/// The lock <c>init</c> or <c>update</c> would write for the running engine, computed before anything is written: the lock's path
/// and canonical text and, for a committed lock, the install script to vendor beside it. <see cref="Notes"/> say how the hashes
/// were checked; without a lock, <see cref="Reason"/> says why none is written (the project then keeps its version pin only).
/// </summary>
internal sealed record EngineLockPlan(string? LockPath, EngineLock? Lock, IReadOnlyList<string> Notes, string? Reason)
{
    /// <summary>Whether the lock is the committed one, which comes with the vendored install script.</summary>
    public bool Committed { get; init; }

    public static EngineLockPlan None(string reason) => new(null, null, [], reason);
}
