using System.Text;
using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hooks.Requests;
using Lockflight.Cli.Ledger;

namespace Lockflight.Cli.Hooks.Events;

/// <summary>
/// Stop: the stop gate (<see cref="StopGate"/>). While the agent waits on a person's approval (<see cref="PendingApproval"/>), it
/// stops cleanly with one message for the person instead of running the gate on tests that may be red on purpose. When the turn
/// does end, it records what the person can now see and offers the approval that waits for them (<see cref="ApprovalOffer"/>),
/// whether the stop gate is on or off; only in a session a person attends, since only there can they see it. An engine error is reported and never bricks the session.
/// </summary>
internal sealed class StopHandler : IHookHandler
{
    private const string StopWaitingSignature = "stop:waiting-for-approval";

    public HookEvent Event => HookEvent.Stop;

    public HookFailurePolicy OnEngineError(HookFailureState state) => HookFailurePolicy.Allow;

    public bool Claims(HookRequest input) => true;

    public async Task<HookReply> DecideAsync(HookInvocation invocation, CancellationToken cancellationToken)
    {
        Manifest manifest = invocation.Manifest;
        if (manifest.StopGate && await PendingApproval.FindAsync(manifest.Root, cancellationToken) is string waiting)
        {
            LedgerEvent skipped = new(LedgerEventType.EvidenceRecorded, StopWaitingSignature, waiting, Detail: null);
            await new LedgerWriter(manifest.Root, invocation.Context.Time).AppendAsync(skipped, cancellationToken);
            string? offer = ApprovalOffer.AtTurnEnd(invocation);
            return new HookReply(HookDecision.Allow, SystemMessage(offer is null ? waiting : $"{waiting}\n{offer}"));
        }

        HookDecision decision = await StopGate.DecideAsync(manifest, invocation.Input.Session.Id, invocation.Context.Time, cancellationToken);
        if (decision.Outcome == HookOutcome.Block)
        {
            // The agent goes on: the turn has not ended, so nothing is recorded as seen.
            return HookReply.Of(decision);
        }

        return ApprovalOffer.AtTurnEnd(invocation) is string ready ? new HookReply(decision, SystemMessage(ready)) : HookReply.Of(decision);
    }

    private static string SystemMessage(string message)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("systemMessage", message);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
