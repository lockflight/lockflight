namespace Lockflight.Cli.Evidence.Blocked;

/// <summary>
/// Why work stopped until a person acts (<c>reason.code</c> of <c>lockflight.blocked/v1</c>). The set grows by addition; a reader
/// shows a code it does not know as it is (<see cref="Unknown"/>) and decides nothing on it.
/// </summary>
internal enum BlockedReasonCode
{
    /// <summary>A code this engine does not know, kept as written.</summary>
    Unknown = 0,

    /// <summary>An artifact the work needs has no approval (<c>approval-missing</c>).</summary>
    ApprovalMissing = 1,

    /// <summary>An approved artifact changed since (<c>approval-stale</c>).</summary>
    ApprovalStale = 2,

    /// <summary>Spec-first applies and the spec is not on the base branch unchanged (<c>spec-not-on-base</c>).</summary>
    SpecNotOnBase = 3,

    /// <summary>The base branch's CODEOWNERS does not cover the spec paths (<c>codeowners-missing</c>).</summary>
    CodeownersMissing = 4,

    /// <summary>The change's class is above what may proceed without a person (<c>class-above-limit</c>).</summary>
    ClassAboveLimit = 5,

    /// <summary>The change needs a protected file changed, which only a person does (<c>protected-change-needed</c>).</summary>
    ProtectedChangeNeeded = 6,

    /// <summary>A baseline must be accepted by a person (<c>baseline-needed</c>).</summary>
    BaselineNeeded = 7,

    /// <summary>A person must answer questions (<c>question</c>).</summary>
    Question = 8,

    /// <summary>The run's budget ran out (<c>budget-exhausted</c>).</summary>
    BudgetExhausted = 9,

    /// <summary>The run's time ran out (<c>time-exhausted</c>).</summary>
    TimeExhausted = 10,

    /// <summary>The configuration is invalid (<c>config-invalid</c>).</summary>
    ConfigInvalid = 11,

    /// <summary>The engine is not the trusted one (<c>engine-untrusted</c>).</summary>
    EngineUntrusted = 12,

    /// <summary>Access the work needs is missing (<c>access-needed</c>).</summary>
    AccessNeeded = 13,
}
