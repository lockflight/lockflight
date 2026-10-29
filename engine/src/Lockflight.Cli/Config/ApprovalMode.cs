namespace Lockflight.Cli.Config;

/// <summary>Where a feature's spec is approved by default (<c>approvals.default</c>).</summary>
internal enum ApprovalMode
{
    /// <summary>The spec and the code arrive in the same pull request, approved by its review (<c>same-pr</c>).</summary>
    SamePr = 1,

    /// <summary>The spec is merged on the base branch before a pull request with code (<c>spec-first</c>).</summary>
    SpecFirst = 2,
}
