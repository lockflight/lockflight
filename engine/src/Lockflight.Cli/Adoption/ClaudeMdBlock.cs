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

        This project is gated by Lockflight: checks, not instructions, decide when work is done. `lockflight` below is
        `dotnet lockflight`, or `~/.lockflight/bin/lockflight` when Lockflight was installed from source.

        - **Done means a gate verdict.** Before you report a task done or commit, run `lockflight gate run` and report its
          verdict. FAIL or NOT-EARNED means not done.
        - **Protected files** (`lockflight.yaml`, `.lockflight/`, Claude Code settings, hooks, agents, skills and commands, build
          and package config, each unit's control files) are changed by a person or a `lockflight` command; the guard blocks
          your edits. When a task needs one changed (a rename, a package bump), write the exact change as a patch outside the
          project, for example `/tmp/<task>.patch`, and ask the user to review and `git apply` it.
        - **Human decisions** are run by the user in their own terminal; ask, never run them: `lockflight approve` (sign off a
          spec or tasks), `lockflight baseline` (accept build warnings, failing tests or package advisories the project
          already has) and `lockflight redgreen reset` (discard a feature's test-first evidence).
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
