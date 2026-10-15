namespace Lockflight.Cli.Hooks;

/// <summary>
/// The git worktrees Claude Code creates for subagents under a project (<c>.claude/worktrees/&lt;name&gt;</c>, each with a
/// <c>.git</c> file pointing back at the repository). Each is a full checkout of the project.
/// </summary>
internal static class AgentWorktrees
{
    public const string RelativeDirectory = ".claude/worktrees";

    public static IReadOnlyList<string> Under(string root)
    {
        string directory = Path.Combine(root, ".claude", "worktrees");
        if (!Directory.Exists(directory))
        {
            return [];
        }

        try
        {
            return [.. Directory.EnumerateDirectories(directory)
                .Where(worktree => File.Exists(Path.Combine(worktree, ".git")))
                .Order(StringComparer.Ordinal)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
