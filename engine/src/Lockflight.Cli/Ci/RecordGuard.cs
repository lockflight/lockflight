namespace Lockflight.Cli.Ci;

/// <summary>
/// An <c>if</c> on a snapshot variable's value in a scanned method (<see cref="SnapshotRecords"/>): the code from
/// <paramref name="Start"/> up to <paramref name="End"/> runs only when <paramref name="Variable"/> equals a string literal, and
/// nothing jumps into it from outside the guard.
/// </summary>
/// <param name="Variable">The environment variable's name.</param>
/// <param name="Read">The offset of the <c>Environment.GetEnvironmentVariable</c> call that reads it.</param>
/// <param name="Start">The offset of the first guarded instruction.</param>
/// <param name="End">The offset the guard skips to when the value does not match.</param>
internal sealed record RecordGuard(string Variable, int Read, int Start, int End);
