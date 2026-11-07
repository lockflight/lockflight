namespace Lockflight.Cli.Ci;

/// <summary>How a person gated the spec of a structural-only change.</summary>
internal enum StructuralRoute
{
    /// <summary>The spec is added or changed in this pull request, and the base's CODEOWNERS gives its path an owner.</summary>
    Codeowners = 1,

    /// <summary>Spec-first applies, and the spec was merged on the base first and is unchanged here.</summary>
    SpecFirst = 2,
}
