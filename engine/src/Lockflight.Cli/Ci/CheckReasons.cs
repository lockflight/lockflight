using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Reasons;

namespace Lockflight.Cli.Ci;

/// <summary>The reason codes of the judge's own states: a tree that could not be listed, a rejected red class, a test since red, an existing test.</summary>
internal static class CheckReasons
{
    /// <summary>Why a tree gave no inventory: the budget ran out, a tool broke, or its tests could not be listed.</summary>
    public static ReasonCode NotListed(TreeOutcome outcome) => outcome switch
    {
        TreeOutcome.OutOfTime => ReasonCode.CiBudgetExhausted,
        TreeOutcome.Broken => ReasonCode.ToolError,
        _ => ReasonCode.TestsNotListed,
    };

    /// <summary>Why the base gave no inventory to compare with: the budget ran out, a tool broke, or it could not be listed.</summary>
    public static ReasonCode BaseNotListed(TreeOutcome outcome) =>
        NotListed(outcome) is ReasonCode.TestsNotListed ? ReasonCode.BaseNotCompared : NotListed(outcome);

    /// <summary>The code of a rejected red class; <see cref="ReasonCode.None"/> for an accepted one.</summary>
    public static ReasonCode Of(RedClass redClass) => redClass switch
    {
        RedClass.RedOther => ReasonCode.RedOther,
        RedClass.RedInTest => ReasonCode.RedInTest,
        RedClass.RedNoProduction => ReasonCode.RedNoProduction,
        RedClass.GreenBefore => ReasonCode.GreenBefore,
        RedClass.Skipped => ReasonCode.Skipped,
        RedClass.Flaky => ReasonCode.Flaky,
        _ => ReasonCode.None,
    };

    /// <summary>The code of a cycle test's state since its red; <see cref="ReasonCode.None"/> for one that still holds.</summary>
    public static ReasonCode Of(SinceRed state) => state switch
    {
        SinceRed.AmendRejected => ReasonCode.AmendRejected,
        SinceRed.AmendWeakened => ReasonCode.AmendWeakened,
        SinceRed.AmendUncheckable => ReasonCode.AmendUncheckable,
        SinceRed.AmendChangedExpectation => ReasonCode.AmendChangedExpectation,
        SinceRed.MissingAtHead => ReasonCode.CycleTestMissing,
        _ => ReasonCode.None,
    };

    /// <summary>The code of an existing test's re-check; <see cref="ReasonCode.None"/> for one that still tells the behaviours apart, or whose old version is kept.</summary>
    public static ReasonCode Of(ExistingChange state) => state switch
    {
        ExistingChange.PassesOnBase => ReasonCode.ExistingTestPassesOnBase,
        ExistingChange.Uncheckable => ReasonCode.ExistingTestUncheckable,
        ExistingChange.OldVersionFails => ReasonCode.ExistingTestOldVersionFails,
        ExistingChange.OldVersionUncheckable => ReasonCode.ExistingTestOldVersionUncheckable,
        _ => ReasonCode.None,
    };
}
