namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>
/// A slot a session command keeps its one-time code in (<c>spec</c>, <c>baseline-tests</c>), for the status line: what waits there
/// (<c>approval</c>) and the line the person types to use it, given what the code was issued for.
/// </summary>
internal sealed record SessionCodeSlot(string Slot, string What, Func<SessionCodeWaiting, string> Line);
