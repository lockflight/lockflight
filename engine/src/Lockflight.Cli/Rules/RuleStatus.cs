namespace Lockflight.Cli.Rules;

/// <summary>
/// What backs a written rule. None of these is "enforced": Lockflight's local controls are guardrails, and a boundary needs a
/// required CI check the repository cannot show.
/// </summary>
internal enum RuleStatus
{
    /// <summary>A Lockflight guardrail refuses exactly what the rule forbids (the guard on a protected path, the human-only refusal).</summary>
    Checked = 0,

    /// <summary>A check touches the rule but does not prove it; the audit says what is missing.</summary>
    Related = 1,

    /// <summary>Nothing checks the rule: it is guidance the agent may or may not follow.</summary>
    GuidanceOnly = 2,
}
