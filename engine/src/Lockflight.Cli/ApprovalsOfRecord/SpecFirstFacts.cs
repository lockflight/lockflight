namespace Lockflight.Cli.ApprovalsOfRecord;

/// <summary>
/// The spec-first facts git can show for one pull request: whether spec-first applies and why (<c>approvals.default</c>,
/// <c>specFirstFrom</c> for its class, a <c>specFirstPaths</c> glob a changed path matches), the feature's spec, whether it is on
/// the base branch, and the spec paths (<c>approvals.specPaths</c>) the pull request changes: under spec-first a code pull request
/// changes none, its own feature's spec or another's.
/// </summary>
internal sealed record SpecFirstFacts(bool Applies, IReadOnlyList<string> Because, string? SpecPath, bool SpecOnBase, IReadOnlyList<string> ChangedSpecs)
{
    public static SpecFirstFacts NotApplicable { get; } = new(Applies: false, [], SpecPath: null, SpecOnBase: false, []);

    /// <summary>Spec-first does not apply, or the spec was merged on the base first and this pull request changes no spec.</summary>
    public bool Satisfied => !Applies || (SpecPath is not null && SpecOnBase && ChangedSpecs.Count == 0);
}
