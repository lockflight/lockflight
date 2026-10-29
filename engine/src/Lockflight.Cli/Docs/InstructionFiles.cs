namespace Lockflight.Cli.Docs;

/// <summary>
/// The files an agent takes instructions from, which the pr profile's <c>docs.references</c> step reads: <c>CLAUDE.md</c> and
/// <c>AGENTS.md</c> at any depth, and the Markdown under <c>.claude/rules/</c>, <c>.claude/skills/</c>, <c>.claude/agents/</c> and
/// <c>.claude/commands/</c>.
/// </summary>
internal static class InstructionFiles
{
    public const string AgentsMd = "AGENTS.md";

    private static readonly string[] ClaudeFolders = ["rules", "skills", "agents", "commands"];

    public static bool IsInstruction(string path)
    {
        string name = RepoPath.FileName(path);
        if (name is DocumentSelection.ClaudeMd or AgentsMd)
        {
            return true;
        }

        if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] directories = RepoPath.Directory(path).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return directories.Select((segment, index) => segment == ".claude" && index + 1 < directories.Length && ClaudeFolders.Contains(directories[index + 1], StringComparer.Ordinal))
            .Any(found => found);
    }
}
