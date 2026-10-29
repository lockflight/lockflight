namespace Lockflight.Cli.Evidence.Blocked;

/// <summary>The <c>lockflight.blocked/v1</c> spellings of its enums, in one place.</summary>
internal static class BlockedNames
{
    private static readonly (BlockedReasonCode Code, string Name)[] Codes =
    [
        (BlockedReasonCode.ApprovalMissing, "approval-missing"),
        (BlockedReasonCode.ApprovalStale, "approval-stale"),
        (BlockedReasonCode.SpecNotOnBase, "spec-not-on-base"),
        (BlockedReasonCode.CodeownersMissing, "codeowners-missing"),
        (BlockedReasonCode.ClassAboveLimit, "class-above-limit"),
        (BlockedReasonCode.ProtectedChangeNeeded, "protected-change-needed"),
        (BlockedReasonCode.BaselineNeeded, "baseline-needed"),
        (BlockedReasonCode.Question, "question"),
        (BlockedReasonCode.BudgetExhausted, "budget-exhausted"),
        (BlockedReasonCode.TimeExhausted, "time-exhausted"),
        (BlockedReasonCode.ConfigInvalid, "config-invalid"),
        (BlockedReasonCode.EngineUntrusted, "engine-untrusted"),
        (BlockedReasonCode.AccessNeeded, "access-needed"),
    ];

    private static readonly (BlockedWho Who, string Name)[] Whos =
    [
        (BlockedWho.Person, "person"),
        (BlockedWho.Codeowner, "codeowner"),
        (BlockedWho.Maintainer, "maintainer"),
    ];

    private static readonly (BlockedAction Action, string Name)[] Actions =
    [
        (BlockedAction.Approve, "approve"),
        (BlockedAction.Answer, "answer"),
        (BlockedAction.ReviewProtectedChange, "review-protected-change"),
        (BlockedAction.AcceptBaseline, "accept-baseline"),
        (BlockedAction.RaiseLimit, "raise-limit"),
        (BlockedAction.FixConfig, "fix-config"),
        (BlockedAction.GrantAccess, "grant-access"),
        (BlockedAction.UpdateEngine, "update-engine"),
    ];

    public static string ToName(BlockedReasonCode code) => Codes.Single(entry => entry.Code == code).Name;

    public static BlockedReasonCode CodeOf(string? name) => Codes.FirstOrDefault(entry => entry.Name == name).Code;

    public static string ToName(BlockedWho who) => Whos.Single(entry => entry.Who == who).Name;

    public static BlockedWho WhoOf(string? name) => Whos.FirstOrDefault(entry => entry.Name == name).Who;

    public static string ToName(BlockedAction action) => Actions.Single(entry => entry.Action == action).Name;

    public static BlockedAction ActionOf(string? name) => Actions.FirstOrDefault(entry => entry.Name == name).Action;
}
