using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle.Approvals;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// Blocks a tool call that would put an in-session human command (<c>/lockflight:approve</c>, <c>/lockflight:baseline</c>,
/// <c>/lockflight:reset</c>, <c>/lockflight:amend</c>) in front of Claude Code as a prompt: a scheduled task, a wakeup, a message to
/// a session, a loop, a subagent's prompt or a shell command. A prompt Claude Code starts by itself expands slash commands like a
/// typed one and its hook payload looks the same, so the only place to stop the agent from deciding for the person in the session
/// is the call that would queue the prompt. Tools that only read or write files are exempt, so specs and docs that mention the
/// commands can still be edited, except on the files Claude Code reads prompts from (<see cref="PromptQueueFiles"/>): every write
/// there is blocked, by a file tool or a shell command, because a prompt can be put together across several edits.
/// </summary>
internal static class SessionCommandRelay
{
    private static readonly HashSet<string> FileTools = ["Read", "Write", "Edit", "MultiEdit", "NotebookEdit", "Grep", "Glob", "LS"];
    private static readonly HashSet<string> FileWritingTools = ["Write", "Edit", "MultiEdit", "NotebookEdit"];

    public static HookDecision? Decide(HookInput input)
    {
        // A call without a tool name is checked too: only a known file tool is exempt.
        string toolName = input.ToolName ?? "(unnamed tool)";
        if (QueueWrite(input, toolName) is string queue)
        {
            string commands = string.Join(", ", SessionCommands.All.Select(spec => $"/{spec.Name}"));
            return HookDecision.Block(
                "prompt-queue-write",
                $"lockflight: {toolName} would write {queue}, a file Claude Code reads prompts from, where a queued in-session command ({commands}) would not be seen. Use the tool for the purpose (CronCreate, SendMessage), or ask the person to edit the file.");
        }

        if (FileTools.Contains(toolName))
        {
            return null;
        }

        foreach (string value in input.ToolInputStrings)
        {
            if (SessionCommands.MentionedIn(value) is SessionCommandSpec spec)
            {
                return HookDecision.Block(
                    $"{spec.Name.Split(':')[^1]}-relay",
                    $"lockflight: {toolName} would send '/{spec.Name}' as a prompt. Only the person {spec.Decision}: they type /{spec.Name} themselves, or {HumanOnlyCommands.OpenATerminalAndRun($"lockflight {spec.TerminalCommand}")}.");
            }
        }

        return null;
    }

    /// <summary>The prompt queue file the call writes, if any.</summary>
    private static string? QueueWrite(HookInput input, string toolName)
    {
        if (FileWritingTools.Contains(toolName))
        {
            return input.FilePath is string filePath && PromptQueueFiles.Contains(filePath) ? filePath : null;
        }

        ShellDialect? dialect = toolName switch
        {
            "Bash" => ShellDialect.Posix,
            "PowerShell" => ShellDialect.PowerShell,
            _ => null,
        };
        if (dialect is not ShellDialect shell || input.Command is not string command)
        {
            return null;
        }

        ShellWrites writes = ShellWriteTargets.Find(command, shell);
        return writes.Files.Concat(writes.Trees).Concat(writes.FloatingFiles).Concat(writes.FloatingTrees).FirstOrDefault(PromptQueueFiles.Contains);
    }
}
