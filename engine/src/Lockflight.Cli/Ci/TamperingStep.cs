namespace Lockflight.Cli.Ci;

/// <summary>
/// One IL instruction of a scanned method, kept in order so a record-mode guard can be read (<see cref="SnapshotRecords"/>).
/// </summary>
/// <param name="Offset">The instruction's offset in the method body.</param>
/// <param name="Kind">What the instruction is.</param>
/// <param name="Text">The string an <c>ldstr</c> loads, or <c>Type::Member</c> of a call; empty otherwise.</param>
/// <param name="Target">The offset a branch jumps to, or the slot of a local an <c>ldloc</c> or <c>stloc</c> names; 0 otherwise.</param>
/// <param name="Next">The offset of the instruction after this one.</param>
internal sealed record TamperingStep(int Offset, TamperingStepKind Kind, string Text, int Target, int Next);
