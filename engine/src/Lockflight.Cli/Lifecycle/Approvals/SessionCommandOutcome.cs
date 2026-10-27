namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>What the <c>UserPromptExpansion</c> hook does with a typed slash command.</summary>
internal enum SessionCommandOutcome
{
    /// <summary>Not one of the in-session human commands: the expansion goes ahead untouched.</summary>
    Ignored = 0,

    /// <summary>The command is refused; the person sees why and the command for a separate terminal, the agent sees nothing.</summary>
    Refused = 1,

    /// <summary>The digest and a one-time code are shown to the person only; nothing is recorded yet.</summary>
    Shown = 2,

    /// <summary>The decision is recorded; the person gets a confirmation and the agent a note to re-read the engine's state.</summary>
    Recorded = 3,

    /// <summary>
    /// Nothing is recorded, and the person sees why and what to type (a usage hint, a wrong or expired code, changed content).
    /// The person's own typing: ledgered as answered, not as a guard block.
    /// </summary>
    Answered = 4,
}
