namespace Lockflight.Cli.Hooks;

/// <summary>
/// Tighten-only lockdown (0.1 replacement for a kill switch): while <c>.lockflight/lockdown</c> exists, only reading is allowed.
/// Anyone may create the file; removing it is a protected-path write, so the agent cannot lift a lockdown.
/// </summary>
internal static class LockdownPolicy
{
    public const string RelativeMarkerPath = ".lockflight/lockdown";

    public static bool IsActive(string projectRoot) => File.Exists(Path.Combine(projectRoot, RelativeMarkerPath));

    public static HookDecision Decide(HookInput input) =>
        ReadOnlyTools.Allows(input)
            ? HookDecision.Allow
            : HookDecision.Block("lockdown", $"lockflight: the project is in lockdown ({RelativeMarkerPath} exists); only reading is allowed until a human removes it.");
}
