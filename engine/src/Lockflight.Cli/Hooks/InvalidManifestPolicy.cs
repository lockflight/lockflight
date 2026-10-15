using Lockflight.Cli.Config;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// While <c>lockflight.yaml</c> is invalid the guards cannot know the rules, so only reading is allowed; everything else is
/// blocked with the parse error. The manifest is protected, so a human fixes it.
/// </summary>
internal static class InvalidManifestPolicy
{
    public static HookDecision Decide(HookEvent hookEvent, HookInput input, ManifestError error) =>
        hookEvent != HookEvent.PreToolUse || ReadOnlyTools.Allows(input)
            ? HookDecision.Allow
            : HookDecision.Block("invalid-manifest", $"lockflight: {error.Describe()}. Only reading is allowed until a human fixes {Manifest.FileName}.");
}
