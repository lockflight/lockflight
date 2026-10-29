namespace Lockflight.Cli.Hooks.Events;

/// <summary>
/// The one place a hook event's handler is registered. <see cref="HookEvent"/> stays an enum (the set is Claude Code's); a test
/// requires exactly one handler for every member but <see cref="HookEvent.Unknown"/>, which the hook command blocks itself.
/// </summary>
internal static class HookHandlers
{
    public static IReadOnlyList<IHookHandler> All { get; } =
    [
        new PreToolUseHandler(),
        new LogOnlyHandler(HookEvent.PostToolUse),
        new StopHandler(),
        new LogOnlyHandler(HookEvent.SubagentStop),
        new SessionStartHandler(),
        new UserPromptExpansionHandler(),
        new TaskCompletedHandler(),
    ];

    /// <summary>The event's handler; null for an event this engine does not know.</summary>
    public static IHookHandler? For(HookEvent hookEvent) => All.FirstOrDefault(handler => handler.Event == hookEvent);
}
