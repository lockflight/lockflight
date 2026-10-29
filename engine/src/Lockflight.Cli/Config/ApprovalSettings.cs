namespace Lockflight.Cli.Config;

/// <summary>
/// The manifest's <c>approvals:</c> section. The approval of record is the platform's review; Lockflight checks that the base
/// branch's CODEOWNERS covers <see cref="SpecPaths"/> and, where spec-first applies, that the spec is on the base unchanged.
/// </summary>
/// <param name="Mode">Where specs are approved by default (<c>approvals.default</c>).</param>
/// <param name="SpecFirstFrom">From this class spec-first applies even when <see cref="Mode"/> is same-pr; null never.</param>
/// <param name="SpecFirstPaths">A pull request whose diff touches these globs needs spec-first.</param>
/// <param name="SpecPaths">What counts as the spec for the CODEOWNERS coverage check; never empty.</param>
internal sealed record ApprovalSettings(
    ApprovalCheckMode Check, ApprovalMode Mode, ChangeClass? SpecFirstFrom, IReadOnlyList<string> SpecFirstPaths, IReadOnlyList<string> SpecPaths)
{
    public const string DefaultSpecPath = "specs/**";

    /// <summary>A version 2 manifest without the section, or without a key of it.</summary>
    public static ApprovalSettings Default { get; } = new(ApprovalCheckMode.Fail, ApprovalMode.SamePr, SpecFirstFrom: null, [], [DefaultSpecPath]);

    /// <summary>
    /// A version 1 manifest: the check only reports, so an engine upgrade never turns a green repository red by itself.
    /// </summary>
    public static ApprovalSettings ForVersion1 { get; } = Default with { Check = ApprovalCheckMode.Report };
}
