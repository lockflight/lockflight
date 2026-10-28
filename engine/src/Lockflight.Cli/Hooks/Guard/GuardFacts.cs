using Lockflight.Cli.Config;
using Lockflight.Cli.Lifecycle.RedGreen;

namespace Lockflight.Cli.Hooks.Guard;

/// <summary>
/// What the guard rules judge one tool call by, for one root (the project, or one of its agent worktrees, whose
/// <see cref="Manifest"/> has that root). Each fact is computed the first time a rule asks for it, so a rule that blocks early
/// spares the rest the cost (and the risk) of scanning test locks and parsing the command.
/// </summary>
internal sealed class GuardFacts(HookInput input, Manifest manifest)
{
    private static readonly HashSet<string> FileEditingTools = ["Edit", "Write", "MultiEdit", "NotebookEdit"];

    private IReadOnlyList<string>? protectedGlobs;
    private IReadOnlyList<(string Glob, string Reason)>? testLocks;
    private IReadOnlySet<string>? amendedFiles;
    private ShellWrites? writes;

    public HookInput Input => input;

    public Manifest Manifest => manifest;

    public ShellDialect Dialect => input.ToolName == "PowerShell" ? ShellDialect.PowerShell : ShellDialect.Posix;

    /// <summary>Whether the tool edits one literal path (<c>Edit</c>, <c>Write</c>, …) rather than running a command.</summary>
    public bool IsFileEditingTool => input.ToolName is string toolName && FileEditingTools.Contains(toolName);

    /// <summary>The built-in protected paths plus the manifest's rules.</summary>
    public IReadOnlyList<string> ProtectedGlobs => protectedGlobs ??= DefaultProtectedPaths.For(manifest);

    /// <summary>The test globs an open red cycle locks, with the reason.</summary>
    public IReadOnlyList<(string Glob, string Reason)> TestLocks => testLocks ??= TestLock.LockedGlobs(manifest);

    /// <summary>The test files a person's open amend made writable; empty when nothing is locked.</summary>
    public IReadOnlySet<string> AmendedFiles => amendedFiles ??= TestLocks.Count == 0 ? new HashSet<string>() : TestLock.AmendedFiles(manifest);

    /// <summary>What the call writes: the edited file, or the targets parsed from its shell command.</summary>
    public ShellWrites Writes => writes ??= WritesOf(input);

    private static ShellWrites WritesOf(HookInput input)
    {
        if (input.ToolName is not string toolName)
        {
            return ShellWrites.None;
        }

        if (FileEditingTools.Contains(toolName) && input.FilePath is string filePath)
        {
            return ShellWrites.OfFile(filePath);
        }

        if (input.Command is not string command)
        {
            return ShellWrites.None;
        }

        return toolName switch
        {
            "Bash" => ShellWriteTargets.Find(command, ShellDialect.Posix),
            "PowerShell" => ShellWriteTargets.Find(command, ShellDialect.PowerShell),
            _ => ShellWrites.None,
        };
    }
}
