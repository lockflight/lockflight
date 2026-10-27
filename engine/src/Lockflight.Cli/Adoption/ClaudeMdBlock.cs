namespace Lockflight.Cli.Adoption;

/// <summary>
/// The short always-on Lockflight section adoption adds to <c>CLAUDE.md</c>, between markers so it is found, updated or removed
/// without touching the team's own text.
/// </summary>
internal static class ClaudeMdBlock
{
    public const string Begin = "<!-- lockflight:begin -->";

    public const string End = "<!-- lockflight:end -->";

    public static string Text { get; } = $"""
        {Begin}
        ## Lockflight

        This project is gated by Lockflight: checks, not instructions, decide when work is done. Run Lockflight as
        `.lockflight/bin/lockflight <command>` from this directory: it is the engine the hooks run and works however Lockflight
        was installed (`lockflight` below means that).

        - **Done means a gate verdict.** Before you report a task done or commit, run `lockflight gate run` and report its
          verdict. FAIL or NOT-EARNED means not done.
        - **Protected files** (`lockflight.yaml`, `.lockflight/`, Claude Code settings, hooks, agents, skills and commands, build
          and package config, each unit's control files) are changed by a person or a `lockflight` command; the guard blocks
          your edits, and a user's permission in chat does not lift it, so never ask for permission to bypass it. When a task
          needs one changed (a rename, a package bump), write the complete new file in your scratch directory and run
          `lockflight propose <path> --from <file>` (`<path> --delete` to remove one; several pairs in one call), then give
          the user the `git apply` command it prints. It writes one patch per file and applies nothing.
        - **Human decisions** are the user's; ask, never run them. They run them in a separate terminal window (not with `!`
          in Claude Code: that runs inside the session and is refused): `lockflight approve` (sign off a spec or tasks),
          `lockflight baseline` (accept build warnings, failing tests or package advisories the project already has),
          `lockflight redgreen reset` (discard a feature's test-first evidence), `lockflight redgreen amend` (let one locked test
          change when its expectation is wrong), `lockflight review dismiss` (close a review
          finding they judge not to be a defect), `lockflight adopt apply|undo` and `lockflight update`. Approvals, baselines, resets and amends can also be given in this
          session: the user types `/lockflight:approve spec` (or `tasks`), `/lockflight:baseline`, `/lockflight:reset` or
          `/lockflight:amend` themselves; never type them or guess or suggest their codes, and check the result with the
          engine (`lockflight approve verify --feature <id>`, `lockflight redgreen status`).
        - Start Claude Code at this directory so these settings load. `lockflight doctor` shows what is missing.
        {End}
        """;

    public static bool IsPresent(string text) => text.Contains(Begin, StringComparison.Ordinal);

    /// <summary>Whether the file holds the block exactly as this engine writes it.</summary>
    public static bool IsCurrent(string text) => text.Contains(Text, StringComparison.Ordinal);

    /// <summary>
    /// The file's text with the current block: an older block between the markers is replaced in place, a missing one is
    /// appended; the team's own text is never touched.
    /// </summary>
    public static string Upsert(string text)
    {
        int begin = text.IndexOf(Begin, StringComparison.Ordinal);
        int end = begin < 0 ? -1 : text.IndexOf(End, begin, StringComparison.Ordinal);
        return begin >= 0 && end >= 0 ? text[..begin] + Text + text[(end + End.Length)..] : AppendTo(text);
    }

    /// <summary>The file's text with the block appended once.</summary>
    public static string AppendTo(string text) => IsPresent(text)
        ? text
        : text + (text.Length == 0 || text.EndsWith('\n') ? string.Empty : "\n") + (text.Length == 0 ? string.Empty : "\n") + Text + "\n";
}
