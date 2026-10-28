namespace Lockflight.Cli.Docs;

/// <summary>
/// Which tracked Markdown files the drift check reads: the instructions an agent follows (<c>CLAUDE.md</c>, <c>.claude/rules/</c>,
/// the Spec Kit constitution, skills), the project's <c>docs/</c> and its root <c>README.md</c>. Files that record history are
/// left out, because they are expected to name what is gone: changelogs, decision records, Spec Kit feature folders, and the
/// skills Spec Kit installs and refreshes itself.
/// </summary>
internal static class DocumentSelection
{
    /// <summary>Claude Code's instruction file, at any depth.</summary>
    public const string ClaudeMd = "CLAUDE.md";

    private const string Markdown = ".md";

    private static readonly string[] HistoryDirectories = ["decisions", "adr", "specs"];

    public static bool IsScanned(string path)
    {
        if (!path.EndsWith(Markdown, StringComparison.OrdinalIgnoreCase) || IsHistory(path))
        {
            return false;
        }

        string name = RepoPath.FileName(path);
        string[] directories = RepoPath.Directory(path).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return name == ClaudeMd
            || path == "README.md"
            || path.StartsWith("docs/", StringComparison.Ordinal)
            || path.EndsWith(".specify/memory/constitution.md", StringComparison.Ordinal)
            || ContainsPair(directories, ".claude", "rules")
            || IsSkillFile(directories);
    }

    /// <summary>
    /// Whether the rules audit reads the file: the instructions an agent is given (<c>CLAUDE.md</c>, <c>.claude/rules/</c>, the
    /// constitution, skills), not the project's other docs.
    /// </summary>
    public static bool IsRuleSource(string path)
    {
        if (!path.EndsWith(Markdown, StringComparison.OrdinalIgnoreCase) || IsHistory(path))
        {
            return false;
        }

        string[] directories = RepoPath.Directory(path).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return RepoPath.FileName(path) == ClaudeMd
            || path.EndsWith(".specify/memory/constitution.md", StringComparison.Ordinal)
            || ContainsPair(directories, ".claude", "rules")
            || IsSkillFile(directories);
    }

    /// <summary>Whether the file is a skill's <c>SKILL.md</c>, for the size budget.</summary>
    public static bool IsSkill(string path) => RepoPath.FileName(path) == "SKILL.md" && IsSkillFile(RepoPath.Directory(path).Split('/'));

    private static bool IsHistory(string path)
    {
        string[] segments = path.Split('/');
        return segments[^1].Equals("CHANGELOG.md", StringComparison.OrdinalIgnoreCase)
            || segments[..^1].Any(segment => HistoryDirectories.Contains(segment, StringComparer.OrdinalIgnoreCase))
            || segments.Select((segment, index) => index > 0 && segments[index - 1] == "skills" && segment.StartsWith("speckit-", StringComparison.Ordinal)).Any(found => found);
    }

    /// <summary>A file anywhere under <c>skills/&lt;name&gt;/</c>.</summary>
    private static bool IsSkillFile(string[] directories) =>
        directories.Select((segment, index) => segment == "skills" && index + 1 < directories.Length).Any(found => found);

    private static bool ContainsPair(string[] directories, string first, string second) =>
        directories.Select((segment, index) => segment == first && index + 1 < directories.Length && directories[index + 1] == second).Any(found => found);
}
