using Lockflight.Cli.Config;
using Lockflight.Cli.Gates.Baselines;
using Lockflight.Cli.Hooks.Requests;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle.Approvals;
using Lockflight.Cli.Lifecycle.RedGreen;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// The human decisions a person can give by typing a slash command in Claude Code: <c>/lockflight:approve</c>,
/// <c>/lockflight:baseline</c>, <c>/lockflight:reset</c> and <c>/lockflight:amend</c>. The <c>UserPromptExpansion</c> hook routes
/// each to its engine side, and never passes one on to the agent when the engine fails.
/// </summary>
internal static class SessionCommands
{
    public static IReadOnlyList<SessionCommandSpec> All { get; } = [SessionApproval.Spec, SessionBaseline.Spec, SessionReset.Spec, SessionAmend.Spec];

    /// <summary>The command the payload expands (any letter case); null for every other slash command.</summary>
    public static SessionCommandSpec? Of(HookRequest input) => All.FirstOrDefault(spec => SessionCommandGate.Names(input, spec));

    /// <summary>The first command whose name appears in the raw payload (any letter case); null when none does.</summary>
    public static SessionCommandSpec? MentionedIn(string text) => All.FirstOrDefault(spec => text.Contains(spec.Name, StringComparison.OrdinalIgnoreCase));

    public static Task<SessionCommandResult> DecideAsync(SessionCommandSpec spec, HookRequest input, CliContext context, Manifest manifest, CancellationToken cancellationToken) =>
        spec.Create(context, manifest).DecideAsync(input, cancellationToken);

    /// <summary>
    /// For the status line: the line to type for a code that waits and is still valid (newest first), as
    /// <c>approval waiting: type /lockflight:approve spec &lt;code&gt;</c>; null when none waits. Only a hash of the code is kept, so the
    /// line names it as a placeholder.
    /// </summary>
    public static string? WaitingLine(Manifest manifest, TimeProvider time, RandomSource random)
    {
        SessionCodes codes = new(manifest.Root, time, random);
        IEnumerable<SessionCodeSlot> slots = All.SelectMany(spec => spec.Slots);
        return slots
            .Select(slot => (slot, waiting: codes.Waiting(slot.Slot, _ => true)))
            .Where(entry => entry.waiting is not null)
            .OrderByDescending(entry => entry.waiting!.ExpiresAt)
            .Select(entry => $"{entry.slot.What} waiting: type {entry.slot.Line(entry.waiting!)}")
            .FirstOrDefault();
    }

    /// <summary>What the person is told when the engine fails on the command: nothing was done, and how to do it in a terminal.</summary>
    public static string InternalError(SessionCommandSpec spec, string message) =>
        $"lockflight: {spec.NothingDone}: internal error: {message}. {HumanOnlyCommands.Sentence(HumanOnlyCommands.OpenATerminalAndRun($"lockflight {spec.TerminalCommand}"))} instead.";
}
