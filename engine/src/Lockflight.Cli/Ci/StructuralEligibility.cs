namespace Lockflight.Cli.Ci;

/// <summary>
/// Whether a pull request whose spec is all <c>[structural]</c> is judged as a structural-only change (no red commit; trace and the
/// existing tests required instead): the conditions it does not meet (none when eligible), how a person gated the spec (null when
/// none did), the spec and its requirement IDs.
/// </summary>
internal sealed record StructuralEligibility(
    IReadOnlyList<StructuralUnmet> Unmet,
    StructuralRoute? Route,
    string SpecPath,
    string FeatureId,
    IReadOnlyList<string> Requirements)
{
    public bool Eligible => Unmet.Count == 0;
}
