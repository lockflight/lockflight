namespace Lockflight.Cli.Learning;

/// <summary>Deterministic pre-classification of a recurring incident; a person confirms it.</summary>
internal enum IncidentClass
{
    /// <summary>A control blocked the agent: the rule exists and was enforced, but the agent keeps reaching for it.</summary>
    RuleIgnored = 1,

    /// <summary>The code failed a check the agent could have run or known about earlier (build, format, tests, review).</summary>
    MissingKnowledge = 2,

    /// <summary>The environment or the tools broke (missing runtime, network, timeouts) — not the agent's code.</summary>
    ToolingGap = 3,

    /// <summary>The test-first discipline was not followed (rejected reds).</summary>
    ProcessSkipped = 4,
}
