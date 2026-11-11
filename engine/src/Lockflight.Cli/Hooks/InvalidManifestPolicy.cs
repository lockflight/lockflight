using Lockflight.Cli.Config;
using Lockflight.Cli.Hooks.Requests;
using Lockflight.Cli.Lifecycle.Approvals;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// While <c>lockflight.yaml</c> is invalid the guards cannot know the rules, so only reading is allowed; everything else is
/// blocked with the parse error. The manifest is protected, so a human fixes it. A typed in-session human command
/// (<c>/lockflight:approve</c>) is refused too, as when the engine fails: it must never reach the agent as a prompt.
/// </summary>
internal static class InvalidManifestPolicy
{
    public static HookDecision Decide(HookEvent hookEvent, HookRequest input, ManifestError error)
    {
        if (hookEvent == HookEvent.UserPromptExpansion && SessionCommands.Of(input) is SessionCommandSpec spec)
        {
            return HookDecision.Block("invalid-manifest", $"lockflight: /{spec.Name} was not run: {error.Describe()}. Fix {Manifest.FileName}, then type the command again.");
        }

        return hookEvent != HookEvent.PreToolUse || ReadOnlyTools.Allows(input)
            ? HookDecision.Allow
            : HookDecision.Block("invalid-manifest", $"lockflight: {error.Describe()}. Only reading is allowed until a human fixes {Manifest.FileName}.");
    }
}
