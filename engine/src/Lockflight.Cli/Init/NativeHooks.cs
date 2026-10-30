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
    private const string LinkedEngine = ".lockflight/bin/lockflight";
    private const string Lockdown = LockdownPolicy.RelativeMarkerPath;

    /// <summary>Each Claude Code event with the engine's name for it, its time limit in seconds, and whether a failure blocks.</summary>
    private static readonly (string Event, string EngineEvent, string? Matcher, int Timeout, bool BlockOnFailure)[] Events =
    [
        ("SessionStart", "session-start", null, 10, false),
        ("PreToolUse", "pre-tool-use", ".*", 10, true),
        ("TaskCompleted", "task-completed", null, 30, false),
        ("Stop", "stop", null, 600, false),
    ];

    /// <summary>
    /// The command for one engine event, with paths from the project root Claude Code passes in <c>CLAUDE_PROJECT_DIR</c>. The
    /// native level needs no engine on the machine: with neither a linked engine nor a lockdown it does nothing, and the deny
    /// rules and the sandbox still hold. A teammate who links the engine gets the guard, as at the plugin level.
    /// </summary>
    public static string Command(string engineEvent) =>
        $"[ -e \"{Project(LinkedEngine)}\" ] || [ -e \"{Project(LinkedEngine)}.exe\" ] || [ -e \"{Project(Lockdown)}\" ] || exit 0; "
        + $"{EnvironmentVariables.HookAdapter}={ClaudeCodeAdapter.ClientName} sh \"{Project(NativeHookShim.RelativePath)}\" {engineEvent}";

    /// <summary>Whether a hook command is one of Lockflight's native hooks (it runs the vendored shim).</summary>
    public static bool IsLockflights(string? command) => command?.Contains(NativeHookShim.RelativePath, StringComparison.Ordinal) == true;

    /// <summary>Whether the settings hold Lockflight's native hook for every event it writes, as this engine writes them.</summary>
    public static bool AreCurrent(JsonNode? settings) =>
        Events.All(entry => Find(settings?[HooksKey]?[entry.Event] as JsonArray)?[CommandKey]?.GetValue<string>() == Command(entry.EngineEvent));

    /// <summary>Whether the settings hold any of Lockflight's native hooks.</summary>
    public static bool AnyPresent(JsonNode? settings) =>
        Events.Any(entry => Find(settings?[HooksKey]?[entry.Event] as JsonArray) is not null);

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
    /// Adds the hook for each event that has none, and brings one Lockflight wrote earlier up to date in place; every other hook
    /// is kept. True when something changed.
    /// </summary>
    public static bool Merge(JsonObject settings)
    {
        if (settings[HooksKey] is not JsonObject hooks)
        {
            hooks = [];
            settings[HooksKey] = hooks;
        }

        bool changed = false;
        foreach ((string name, string engineEvent, string? matcher, int timeout, bool blockOnFailure) in Events)
        {
            if (hooks[name] is not JsonArray groups)
            {
                groups = [];
                hooks[name] = groups;
            }

            JsonObject wanted = Hook(engineEvent, timeout, blockOnFailure);
            if (Find(groups) is JsonObject existing)
            {
                if (!JsonNode.DeepEquals(existing, wanted))
                {
                    foreach (string key in existing.Select(property => property.Key).ToArray())
                    {
                        existing.Remove(key);
                    }

                    foreach ((string key, JsonNode? value) in wanted.ToArray())
                    {
                        wanted.Remove(key);
                        existing[key] = value;
                    }

                    changed = true;
                }

                continue;
            }

            JsonObject group = [];
            if (matcher is not null)
            {
                group[MatcherKey] = matcher;
            }

            group[EntriesKey] = new JsonArray(wanted);
            groups.Add((JsonNode)group);
            changed = true;
        }

        return changed;
    }

    private static string Project(string relativePath) => $"$CLAUDE_PROJECT_DIR/{relativePath}";

    private static JsonObject Hook(string engineEvent, int timeout, bool blockOnFailure)
    {
        JsonObject hook = new()
        {
            ["type"] = "command",
            [CommandKey] = Command(engineEvent),
            ["timeout"] = timeout,
        };
        if (blockOnFailure)
        {
            hook["onFailure"] = "block";
        }

        return hook;
    }

    /// <summary>Lockflight's hook among an event's groups; null when there is none.</summary>
    private static JsonObject? Find(JsonArray? groups) =>
        groups?.OfType<JsonObject>()
            .SelectMany(group => group[EntriesKey] as JsonArray ?? [])
            .OfType<JsonObject>()
            .FirstOrDefault(hook => hook[CommandKey]?.GetValueKind() == JsonValueKind.String && IsLockflights(hook[CommandKey]!.GetValue<string>()));
}
