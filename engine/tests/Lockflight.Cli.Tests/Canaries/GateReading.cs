using Lockflight.Cli.Gates;

namespace Lockflight.Cli.Tests.Canaries;

/// <summary>
/// What a gate run on the canary project reported: its test step (null when the run stopped before it), its last step, and why there
/// is no result at all when it wrote none.
/// </summary>
internal sealed record GateReading(GateStep? Test, GateStep? Last, string? Problem)
{
    public string Describe() =>
        Problem ?? (Last is GateStep last ? $"{last.Name}: {VerdictNames.ToContractName(last.Verdict)} ({last.Detail})" : "the gate ran no step");
}
