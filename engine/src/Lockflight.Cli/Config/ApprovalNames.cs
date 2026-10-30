namespace Lockflight.Cli.Config;

/// <summary>The manifest and evidence spellings of the <c>approvals:</c> settings: <c>fail</c>/<c>report</c> and <c>same-pr</c>/<c>spec-first</c>.</summary>
internal static class ApprovalNames
{
    public static string ToName(ApprovalCheckMode check) => check switch
    {
        ApprovalCheckMode.Fail => "fail",
        ApprovalCheckMode.Report => "report",
        _ => throw new ArgumentOutOfRangeException(nameof(check), check, "Unknown approvals check mode."),
    };

    public static string ToName(ApprovalMode mode) => mode switch
    {
        ApprovalMode.SamePr => "same-pr",
        ApprovalMode.SpecFirst => "spec-first",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown approvals mode."),
    };
}
