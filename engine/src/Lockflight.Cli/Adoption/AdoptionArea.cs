namespace Lockflight.Cli.Adoption;

/// <summary>What part of an existing setup an adoption plan item is about.</summary>
internal enum AdoptionArea
{
    /// <summary>A Claude Code hook in the project's, a unit's or the user's settings.</summary>
    Hook = 1,

    /// <summary>A skill under <c>.claude/skills/</c>.</summary>
    Skill = 2,

    /// <summary>A subagent under <c>.claude/agents/</c>.</summary>
    Agent = 3,

    /// <summary>A slash command under <c>.claude/commands/</c>.</summary>
    Command = 4,

    /// <summary>The Spec Kit installation under <c>.specify/</c>.</summary>
    SpecKit = 5,

    /// <summary>A feature under <c>specs/</c> that predates Lockflight.</summary>
    Feature = 6,

    /// <summary>The always-loaded <c>CLAUDE.md</c>.</summary>
    ClaudeMd = 7,

    /// <summary>A directory that looks like a unit of a workspace (its own repository, a solution, a package.json).</summary>
    Unit = 8,
}
