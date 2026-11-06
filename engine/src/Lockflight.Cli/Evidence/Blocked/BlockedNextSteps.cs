using Lockflight.Cli.Reasons;

namespace Lockflight.Cli.Evidence.Blocked;

/// <summary>
/// What a person does about each <see cref="BlockedReasonCode"/>, for whatever shows a blocked report: the report names no command
/// (a file an agent writes cannot be trusted to carry one), so the Next line is derived here from the code alone. Each code has
/// an entry in <see cref="ReasonCatalogue.GuideUrl"/>, anchored by its contract name.
/// </summary>
internal static class BlockedNextSteps
{
    /// <summary>The Next line; empty for <see cref="BlockedReasonCode.Unknown"/>, which has no entry.</summary>
    public static string Next(BlockedReasonCode code) => code switch
    {
        BlockedReasonCode.ApprovalMissing => "A person reads the artifact and approves it: `/lockflight:approve` typed in Claude Code, or `approve spec` / `approve tasks` in a separate terminal.",
        BlockedReasonCode.ApprovalStale => "The approved file changed since: a person reads it again and approves it again (`/lockflight:approve`).",
        BlockedReasonCode.SpecNotOnBase => ReasonCatalogue.Next(ReasonCode.SpecNotOnBase),
        BlockedReasonCode.CodeownersMissing => ReasonCatalogue.Next(ReasonCode.CodeownersMissing),
        BlockedReasonCode.ClassAboveLimit => "A person decides whether the change may go ahead at its class: they review it, or change the limit in the target branch's policy.",
        BlockedReasonCode.ProtectedChangeNeeded => "A person makes the protected change the need names, or reviews and merges it; an agent cannot.",
        BlockedReasonCode.BaselineNeeded => "A person accepts the baseline (`/lockflight:baseline <kind>` typed in Claude Code, or `baseline <kind>` in a separate terminal) and merges it in its own pull request first.",
        BlockedReasonCode.Question => "A person answers the report's questions; the recommended option is listed first.",
        BlockedReasonCode.BudgetExhausted => "A person raises the run's budget or narrows the work, then runs it again.",
        BlockedReasonCode.TimeExhausted => ReasonCatalogue.Next(ReasonCode.CiBudgetExhausted),
        BlockedReasonCode.ConfigInvalid => "A maintainer fixes the configuration the need names; `doctor` says what is wrong.",
        BlockedReasonCode.EngineUntrusted => "A person installs the engine the project's engine lock pins (`~/.lockflight/bin/lockflight engine install` in a separate terminal); `doctor` explains the mismatch.",
        BlockedReasonCode.AccessNeeded => "A maintainer grants the access the need names (for the CI review: the review job's API key), then the check runs again.",
        _ => string.Empty,
    };

    /// <summary>The entry's anchor: the contract name (<c>approval-missing</c>); null for a code without one.</summary>
    public static string? Anchor(BlockedReasonCode code) => code == BlockedReasonCode.Unknown ? null : BlockedNames.ToName(code);

    public static string? Link(BlockedReasonCode code) => Anchor(code) is string anchor ? $"{ReasonCatalogue.GuideUrl}#{anchor}" : null;
}
