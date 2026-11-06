namespace Lockflight.Cli.Ci;

/// <summary>
/// The conditions, decided from git alone, under which a pull request whose spec is all <c>[structural]</c> needs no red commit
/// (<see cref="StructuralLane"/>).
/// </summary>
internal enum StructuralCondition
{
    /// <summary>A pull request classified by the base branch's routing policy, in a repository that is no unit of a workspace shell.</summary>
    Routed = 1,

    /// <summary>No commit carries a <c>Lockflight-Red</c> trailer, well-formed or not.</summary>
    NoRedCommit = 2,

    /// <summary>A person gates the spec: it changes in this pull request under the base's CODEOWNERS, or spec-first merged it first.</summary>
    SpecReviewed = 3,

    /// <summary>No routing signal that carries behaviour a structural test does not pin (configuration, dependencies, schema, contracts).</summary>
    NoBehaviourSignal = 4,
}
