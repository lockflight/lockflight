using System.Text;
using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hooks.Requests;
using Lockflight.Cli.Ledger;

namespace Lockflight.Cli.Hooks.Events;

/// <summary>
/// Stop: the stop gate (<see cref="StopGate"/>). While the agent waits on a person's approval (<see cref="PendingApproval"/>), it
/// stops cleanly with one message for the person instead of running the gate on tests that may be red on purpose. An engine error
/// is reported and never bricks the session.
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
            return new HookReply(HookDecision.Allow, SystemMessage(waiting));
        }

        HookDecision decision = await StopGate.DecideAsync(manifest, invocation.Input.Session.Id, invocation.Context.Time, cancellationToken);
        return HookReply.Of(decision);
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
