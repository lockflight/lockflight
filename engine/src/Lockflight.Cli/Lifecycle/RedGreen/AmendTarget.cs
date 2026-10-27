namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// What an amend of a test would unlock: the open cycle, the test, the source files that declare it (relative to the root,
/// '/'-separated) and the other tests of the cycle declared in those files, which the amend makes writable too.
/// </summary>
internal sealed record AmendTarget(RedGreenCycle Cycle, RedGreenTest Test, IReadOnlyList<string> Files, IReadOnlyList<string> SharedWith);
