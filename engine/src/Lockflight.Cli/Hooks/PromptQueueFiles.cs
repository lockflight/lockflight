namespace Lockflight.Cli.Hooks;

/// <summary>
/// Files Claude Code reads prompts from and runs as turns: durable scheduled tasks (<c>.claude/scheduled_tasks.json</c>, which it
/// watches), the loop tasks file (<c>.claude/loop.md</c>), routines (<c>.claude/routines/</c>), session mailboxes
/// (<c>.claude/mailbox/</c>) and agent team inboxes (<c>~/.claude/teams/TEAM/inboxes/</c>), in any <c>.claude</c> directory.
/// </summary>
internal static class PromptQueueFiles
{
    private const string ClaudeDirectory = ".claude";
    private const string TeamsDirectory = "teams";
    private const string InboxesDirectory = "inboxes";

    private static readonly string[] QueueFileNames = ["scheduled_tasks.json", "loop.md"];
    private static readonly string[] QueueDirectories = ["mailbox", "routines"];

    /// <summary>Whether the path, as written (relative or absolute, either slash), is one of these files or lies in one of these directories.</summary>
    public static bool Contains(string path)
    {
        string[] segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index < segments.Length - 1; index++)
        {
            if (Is(segments[index], ClaudeDirectory) && IsQueue(segments[(index + 1)..]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsQueue(string[] rest) =>
        (rest.Length == 1 && QueueFileNames.Any(name => Is(rest[0], name)))
        || QueueDirectories.Any(directory => Is(rest[0], directory))
        || (rest.Length >= 3 && Is(rest[0], TeamsDirectory) && Is(rest[2], InboxesDirectory));

    private static bool Is(string segment, string name) => string.Equals(segment, name, StringComparison.OrdinalIgnoreCase);
}
