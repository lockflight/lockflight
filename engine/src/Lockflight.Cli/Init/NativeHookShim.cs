namespace Lockflight.Cli.Init;

/// <summary>
/// The plugin's hook shim (<c>plugins/lockflight/bin/lockflight-hook</c>) as this engine carries it: <c>init --native</c> and
/// <c>update</c> write it into a project as <see cref="RelativePath"/> (a protected path, so the agent cannot edit the guard away), where the project's own Claude Code hooks call it without the
/// plugin. It is the same transport and the same trust checks on a linked engine (owner, the hash record <c>link</c> writes, the
/// engine lock); unlike the plugin, the native hooks do nothing when no engine is linked (<see cref="NativeHooks.Command"/>). A
/// test keeps the embedded copy equal to the plugin's shim.
/// </summary>
internal static class NativeHookShim
{
    public const string RelativePath = ".claude/hooks/lockflight-hook";

    private const string ResourceName = "Lockflight.Cli.lockflight-hook";

    public static string Text { get; } = Load();

    private static string Load()
    {
        using Stream stream = typeof(NativeHookShim).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"the engine was built without {ResourceName}");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
