using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle.Approvals;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// Blocks a tool call that would put an in-session human command (<c>/lockflight:approve</c>, <c>/lockflight:baseline</c>,
/// <c>/lockflight:reset</c>, <c>/lockflight:amend</c>) in front of Claude Code as a prompt: a scheduled task, a wakeup, a message to
/// a session, a loop, a subagent's prompt or a shell command. A prompt Claude Code starts by itself expands slash commands like a
/// typed one and its hook payload looks the same, so the only place to stop the agent from deciding for the person in the session
/// is the call that would queue the prompt. Tools that only read or write files are exempt, so specs and docs that mention the
/// commands can still be edited.
/// </summary>
internal static class SessionCommandRelay
{
    private static readonly HashSet<string> FileTools = ["Read", "Write", "Edit", "MultiEdit", "NotebookEdit", "Grep", "Glob", "LS"];

    public static HookDecision? Decide(HookInput input)
    {
        // A call without a tool name is checked too: only a known file tool is exempt.
        string toolName = input.ToolName ?? "(unnamed tool)";
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
}
