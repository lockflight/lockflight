using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// Blocks a tool call that would put <c>/lockflight:approve</c> in front of Claude Code as a prompt: a scheduled task, a wakeup,
/// a message to a session, a loop, a subagent's prompt or a shell command. A prompt Claude Code starts by itself expands slash
/// commands like a typed one and its hook payload looks the same, so the only place to stop the agent from approving its own work
/// in the session is the call that would queue the prompt. Tools that only read or write files are exempt, so specs and docs that
/// mention the command can still be edited.
/// </summary>
internal static class ApprovalCommandRelay
{
    private const string Command = "lockflight:approve";

    private static readonly HashSet<string> FileTools = ["Read", "Write", "Edit", "MultiEdit", "NotebookEdit", "Grep", "Glob", "LS"];

    public static HookDecision? Decide(HookInput input)
    {
        // A call without a tool name is checked too: only a known file tool is exempt.
        string toolName = input.ToolName ?? "(unnamed tool)";
        if (FileTools.Contains(toolName))
        {
            return null;
        }

        return input.ToolInputStrings.Any(value => value.Contains(Command, StringComparison.OrdinalIgnoreCase))
            ? HookDecision.Block(
                "approve-relay",
                $"lockflight: {toolName} would send '/{Command}' as a prompt. Only the person approves: they type /{Command} themselves, or run lockflight approve {HumanOnlyCommands.WhereToRun}.")
            : null;
    }
}
