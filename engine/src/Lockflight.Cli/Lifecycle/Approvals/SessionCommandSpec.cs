using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>
/// An in-session human command: its slash command without the slash (<c>lockflight:approve</c>), the engine command a person runs
/// instead in a terminal (<c>approve</c>, <c>redgreen reset</c>), what a refusal says was not done, the decision only the person
/// makes (for the guard that keeps the agent from queueing the command), the prefix of a refusal's ledger fingerprint, how to
/// create its engine side for one invocation, and the code slots the status line reports, in its order.
/// </summary>
internal sealed record SessionCommandSpec(
    string Name,
    string TerminalCommand,
    string NothingDone,
    string Decision,
    string RefusalPrefix,
    Func<CliContext, Manifest, ISessionDecider> Create,
    IReadOnlyList<SessionCodeSlot> Slots);
