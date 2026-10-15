namespace Lockflight.Cli.Learning;

/// <summary>The remedy ladder: prefer the most deterministic rung that prevents the incident.</summary>
internal enum RemedyRung
{
    /// <summary>A Roslyn analyzer or compiler setting catches it at build time.</summary>
    Analyzer = 1,

    /// <summary>A gate check, test or policy catches it deterministically.</summary>
    Check = 2,

    /// <summary>A skill teaches the procedure at the moment it is needed.</summary>
    Skill = 3,

    /// <summary>A path-scoped rule loaded only where it applies.</summary>
    PathRule = 4,

    /// <summary>An always-on rule (budgeted; one in, one out).</summary>
    AlwaysOnRule = 5,

    /// <summary>A fix to the environment or tooling (doctor, CI image, timeouts), not to the agent's instructions.</summary>
    Environment = 6,
}
