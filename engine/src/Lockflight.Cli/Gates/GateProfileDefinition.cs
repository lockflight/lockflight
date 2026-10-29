namespace Lockflight.Cli.Gates;

/// <summary>
/// The steps of one profile, in order: once at the root before the units, per unit, once at the root after them. A single
/// repository runs the three lists one after another at its root.
/// </summary>
internal sealed record GateProfileDefinition(
    GateProfile Profile, IReadOnlyList<IGateStep> RootFirst, IReadOnlyList<IGateStep> PerUnit, IReadOnlyList<IGateStep> RootLast)
{
    /// <summary>Every step of the profile in the order a single repository runs them.</summary>
    public IReadOnlyList<IGateStep> InOrder => [.. RootFirst, .. PerUnit, .. RootLast];
}
