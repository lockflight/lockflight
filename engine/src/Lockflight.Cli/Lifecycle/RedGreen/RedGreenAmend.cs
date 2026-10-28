namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// A person's permission to change one test of the open red cycle after its red was accepted (a wrong expectation, an API that
/// changed): who gave it (git <c>user.email</c>), when and why, and the test source files it unlocks, relative to the root and
/// '/'-separated. It stays open, and those files writable, until a red re-checks the test (<see cref="RecheckedAt"/>). That red records
/// the <see cref="Diff"/> of the files since the red-time snapshot; null before it, or for a cycle without a snapshot.
/// </summary>
internal sealed record RedGreenAmend(string TestKey, string MethodFilter, IReadOnlyList<string> Files, string By, DateTimeOffset At, string Reason)
{
    public DateTimeOffset? RecheckedAt { get; init; }

    public AmendDiff? Diff { get; init; }

    public bool IsOpen => RecheckedAt is null;
}
