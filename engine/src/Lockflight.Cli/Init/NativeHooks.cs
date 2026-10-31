using System.Text.Json;
using System.Text.Json.Nodes;
using Lockflight.Cli.Hooks;
using Lockflight.Cli.Hooks.Adapters;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Init;

/// <summary>
/// The Claude Code hooks <c>init --native</c> writes into a project's <c>.claude/settings.json</c>: the plugin's hooks, calling the
/// vendored shim (<see cref="NativeHookShim"/>) instead of the plugin's copy, with the adapter named in
/// <c>LOCKFLIGHT_HOOK_ADAPTER</c> (never an argument: an older engine refuses an extra one). There is no prompt-expansion hook:
/// without the plugin there are no <c>/lockflight:</c> commands to relay. Lockflight's entries are recognised by the shim's path in
/// their command; the team's own hooks stay as and where they are.
/// </summary>
internal static class NativeHooks
{
    public const string HooksKey = "hooks";

    private const string EntriesKey = "hooks";
    private const string CommandKey = "command";
    private const string MatcherKey = "matcher";
    private const string TypeKey = "type";
    private const string CommandType = "command";
    private const string TimeoutKey = "timeout";
    private const string OnFailureKey = "onFailure";
    private const string BlockOnFailure = "block";
    private const string LinkedEngine = ".lockflight/bin/lockflight";
    private const string Lockdown = LockdownPolicy.RelativeMarkerPath;

    private static readonly NativeHookEvent[] Events =
    [
        new("SessionStart", "session-start", Matcher: null, TimeoutSeconds: 10, BlockOnFailure: false),
        new("PreToolUse", "pre-tool-use", Matcher: ".*", TimeoutSeconds: 10, BlockOnFailure: true),
        new("TaskCompleted", "task-completed", Matcher: null, TimeoutSeconds: 30, BlockOnFailure: false),
        new("Stop", "stop", Matcher: null, TimeoutSeconds: 600, BlockOnFailure: false),
    ];

    /// <summary>
    /// The command for one engine event, with paths from the project root Claude Code passes in <c>CLAUDE_PROJECT_DIR</c>. The
    /// native level needs no engine on the machine: with neither a linked engine nor a lockdown it does nothing (it fails open),
    /// says so to the person at session start and at every stop, and the deny rules and the sandbox still hold. Once a teammate links the engine, the shim runs it with the plugin's trust
    /// checks.
    /// </summary>
    public static string Command(string engineEvent) =>
        $"[ -e \"{Project(LinkedEngine)}\" ] || [ -e \"{Project(LinkedEngine)}.exe\" ] || [ -e \"{Project(Lockdown)}\" ] || {{ {NoEngineNotice(engineEvent)}exit 0; }}; "
        + $"{EnvironmentVariables.HookAdapter}={ClaudeCodeAdapter.ClientName} sh \"{Project(NativeHookShim.RelativePath)}\" {engineEvent}";

    /// <summary>What the person reads when the hooks do nothing for want of an engine.</summary>
    public const string NoEngineMessage =
        "Lockflight is OFF in this session: no engine is linked (.lockflight/bin/lockflight), so there is no guard, no test lock and no Stop gate. "
        + "Run ~/.lockflight/bin/lockflight engine install (or dotnet tool restore && dotnet lockflight link) in the project, in a separate terminal window (not with ! in Claude Code), to turn it on.";

    /// <summary>
    /// At session start and at every stop, the no-engine path tells the person (a <c>systemMessage</c>, never the model) that the
    /// checks are off: a deleted <c>.lockflight/bin</c> must not turn the native level off silently. The other events stay quiet.
    /// </summary>
    private static string NoEngineNotice(string engineEvent) =>
        engineEvent is "session-start" or "stop" ? $"printf '%s\\n' '{{\"systemMessage\":\"{NoEngineMessage}\"}}'; " : string.Empty;

    /// <summary>Whether a hook command is one of Lockflight's native hooks (it runs the vendored shim).</summary>
    public static bool IsLockflights(string? command) => command?.Contains(NativeHookShim.RelativePath, StringComparison.Ordinal) == true;

    /// <summary>
    /// Whether the settings hold exactly one Lockflight hook per event, in a group with the event's matcher, as this engine writes
    /// it (command, time limit and failure mode).
    /// </summary>
    public static bool AreCurrent(JsonNode? settings) =>
        Events.All(entry => Ours(Groups(settings, entry.Event)) is [(JsonObject group, JsonObject hook)]
            && JsonNode.DeepEquals(hook, Hook(entry))
            && MatcherOf(group) == entry.Matcher);

    /// <summary>Whether the settings hold any of Lockflight's native hooks.</summary>
    public static bool AnyPresent(JsonNode? settings) => Events.Any(entry => Ours(Groups(settings, entry.Event)).Count > 0);

    /// <summary>
    /// Why the hooks cannot be merged into: <c>hooks</c> is not an object, an event's entry is not a list of objects, or an object's
    /// <c>hooks</c> is not a list of objects. Null when <see cref="Merge"/> may write.
    /// </summary>
    public static string? Problem(JsonObject settings)
    {
        if (settings[HooksKey] is null)
        {
            return null;
        }

        if (settings[HooksKey] is not JsonObject hooks)
        {
            return $"has a \"{HooksKey}\" that is not an object";
        }

        foreach ((string name, JsonNode? groups) in hooks)
        {
            if (groups is not JsonArray list || list.Any(group => group is not JsonObject || (group[EntriesKey] is not null and not JsonArray)))
            {
                return $"has a \"{HooksKey}.{name}\" that is not a list of hook groups";
            }

            if (list.SelectMany(group => group![EntriesKey] as JsonArray ?? []).Any(hook => hook is not JsonObject))
            {
                return $"has a \"{HooksKey}.{name}\" with a hook that is not an object";
            }
        }

        return null;
    }

    /// <summary>
    /// Adds the hook for each event that has none. One Lockflight wrote earlier is brought up to date in place, and moved to a group
    /// of its own when its group's matcher is not the event's; any further copy is removed, so it runs once. Every other hook is
    /// kept. True when something changed. Call only when <see cref="Problem"/> is null.
    /// </summary>
    public static bool Merge(JsonObject settings)
    {
        if (settings[HooksKey] is not JsonObject hooks)
        {
            hooks = [];
            settings[HooksKey] = hooks;
        }

        bool changed = false;
        foreach (NativeHookEvent entry in Events)
        {
            if (hooks[entry.Event] is not JsonArray groups)
            {
                groups = [];
                hooks[entry.Event] = groups;
            }

            List<(JsonObject Group, JsonObject Hook)> ours = Ours(groups);
            if (ours.Count == 1 && JsonNode.DeepEquals(ours[0].Hook, Hook(entry)) && MatcherOf(ours[0].Group) == entry.Matcher)
            {
                continue;
            }

            // Remove every copy (dropping a group it leaves empty) and add the current hook in a group of its own.
            foreach ((JsonObject group, JsonObject hook) in ours)
            {
                JsonArray entries = group[EntriesKey]!.AsArray();
                entries.Remove(hook);
                if (entries.Count == 0)
                {
                    groups.Remove(group);
                }
            }

            JsonObject own = entry.Matcher is null ? [] : new JsonObject { [MatcherKey] = entry.Matcher };
            own[EntriesKey] = new JsonArray(Hook(entry));
            groups.Add((JsonNode)own);
            changed = true;
        }

        return changed;
    }

    private static string Project(string relativePath) => $"$CLAUDE_PROJECT_DIR/{relativePath}";

    private static string? MatcherOf(JsonObject group) =>
        group[MatcherKey]?.GetValueKind() == JsonValueKind.String ? group[MatcherKey]!.GetValue<string>() : null;

    private static JsonObject Hook(NativeHookEvent entry)
    {
        JsonObject hook = new()
        {
            [TypeKey] = CommandType,
            [CommandKey] = Command(entry.EngineEvent),
            [TimeoutKey] = entry.TimeoutSeconds,
        };
        if (entry.BlockOnFailure)
        {
            hook[OnFailureKey] = BlockOnFailure;
        }

        return hook;
    }

    /// <summary>An event's hook groups; null when the settings, their <c>hooks</c> or the event's entry has another shape.</summary>
    private static JsonArray? Groups(JsonNode? settings, string hookEvent) =>
        (settings as JsonObject)?[HooksKey] is JsonObject hooks ? hooks[hookEvent] as JsonArray : null;

    /// <summary>Every Lockflight hook among an event's groups, with the group that holds it.</summary>
    private static List<(JsonObject Group, JsonObject Hook)> Ours(JsonArray? groups) =>
        [.. (groups ?? []).OfType<JsonObject>()
            .SelectMany(group => (group[EntriesKey] as JsonArray ?? []).OfType<JsonObject>().Select(hook => (Group: group, Hook: hook)))
            .Where(pair => pair.Hook[CommandKey]?.GetValueKind() == JsonValueKind.String && IsLockflights(pair.Hook[CommandKey]!.GetValue<string>()))];
}
