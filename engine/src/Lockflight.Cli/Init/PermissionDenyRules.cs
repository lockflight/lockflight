using System.Text.Json;
using System.Text.Json.Nodes;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Init;

/// <summary>
/// <c>permissions.deny</c> rules Lockflight writes into a project's <c>.claude/settings.json</c>: an <c>Edit(…)</c> rule for each
/// protected path, so Claude Code itself refuses the built-in file tools there, before and apart from the guard. In Claude Code an
/// <c>Edit</c> rule covers every built-in tool that edits files (a <c>Write(path)</c> rule never matches), and <c>/path</c> is
/// relative to the session's primary working directory in project settings.
/// </summary>
internal static class PermissionDenyRules
{
    public const string PermissionsKey = "permissions";
    public const string DenyKey = "deny";

    private const string TreeSuffix = "/**";
    private const string LockflightTree = ".lockflight/**";
    private const string LinkedEngine = ".lockflight/bin";

    /// <summary>
    /// The parts of <c>.lockflight/</c> a rule names. Claude Code also adds <c>Edit</c> deny paths to the sandbox's write denials,
    /// and the commands the agent runs write evidence, the ledger and the Stop cache there, so the whole directory is not named.
    /// The engine lock and the vendored install script are written only by <c>init</c> and <c>update</c>.
    /// </summary>
    private static readonly string[] LockflightParts =
        [".lockflight/bin/**", ".lockflight/baselines/**", ".lockflight/features/*/approvals/**", ".lockflight/engine.lock", ".lockflight/ci/**"];

    /// <summary>
    /// One rule per protected path. A pattern with a wildcard inside it is written as it is. A plain path, or a directory
    /// (<c>dir/**</c>), only when it exists: Claude Code adds these rules to the sandbox's write denials, and on Linux and WSL2 the
    /// sandbox holds a denial on a missing file with an empty read-only placeholder while a command runs, which would break a
    /// build that reads <c>global.json</c> or <c>Directory.Build.props</c>. Run <c>init</c> again to add files created since.
    /// </summary>
    public static IReadOnlyList<string> For(string root, IEnumerable<string> protectedGlobs)
    {
        SortedSet<string> rules = new(StringComparer.Ordinal);
        foreach (string glob in protectedGlobs.Select(glob => glob.Replace('\\', '/').TrimStart('/')))
        {
            if (glob.EndsWith(LockflightTree, StringComparison.Ordinal))
            {
                string unit = glob[..^LockflightTree.Length];
                rules.UnionWith(LockflightParts.Select(part => unit + part).Where(part => Applies(root, part)).Select(Rule));
            }
            else if (Applies(root, glob))
            {
                rules.Add(Rule(glob));
            }
        }

        return [.. rules];
    }

    /// <summary>The <c>permissions.deny</c> rules the settings list; none when the list is missing or not a list of strings.</summary>
    public static IReadOnlyList<string> Entries(JsonNode? settings) =>
        settings?[PermissionsKey]?[DenyKey] is JsonArray list && list.All(rule => rule?.GetValueKind() == JsonValueKind.String)
            ? [.. list.Select(rule => rule!.GetValue<string>())]
            : [];

    /// <summary>Why the rules cannot be merged into: <c>permissions</c> is not an object, or its <c>deny</c> not a list of strings.</summary>
    public static string? Problem(JsonObject settings)
    {
        if (settings[PermissionsKey] is null)
        {
            return null;
        }

        if (settings[PermissionsKey] is not JsonObject permissions)
        {
            return $"has a \"{PermissionsKey}\" that is not an object";
        }

        JsonNode? deny = permissions[DenyKey];
        return deny is null || (deny is JsonArray list && list.All(rule => rule?.GetValueKind() == JsonValueKind.String))
            ? null
            : $"has a \"{PermissionsKey}.{DenyKey}\" that is not a list of rules";
    }

    /// <summary>Appends the rules that are missing; the team's rules stay as and where they are. True when something was added.</summary>
    public static bool Merge(JsonObject settings, IReadOnlyList<string> rules)
    {
        if (settings[PermissionsKey] is not JsonObject permissions)
        {
            permissions = [];
            settings[PermissionsKey] = permissions;
        }

        if (permissions[DenyKey] is not JsonArray deny)
        {
            deny = [];
            permissions[DenyKey] = deny;
        }

        HashSet<string> present = [.. deny.Select(rule => rule!.GetValue<string>())];
        bool added = false;
        foreach (string rule in rules.Where(present.Add))
        {
            deny.Add((JsonNode)JsonValue.Create(rule));
            added = true;
        }

        return added;
    }

    /// <summary>
    /// Whether a rule for the glob is written: always for a pattern, else when the path exists. The settings file counts as present
    /// (the rules are being written into it), and so does the linked engine, which init links next.
    /// </summary>
    private static bool Applies(string root, string glob)
    {
        string fixedPart = glob.EndsWith(TreeSuffix, StringComparison.Ordinal) ? glob[..^TreeSuffix.Length] : glob;
        return PathWildcards.HasWildcard(fixedPart) || fixedPart is ClaudeSettingsMerger.RelativePath or LinkedEngine
            || File.Exists(Path.Combine(root, fixedPart)) || Directory.Exists(Path.Combine(root, fixedPart));
    }

    /// <summary>The project-anchored <c>Edit</c> rule for a root-relative glob: <c>global.json</c> becomes <c>Edit(/global.json)</c>.</summary>
    private static string Rule(string glob) => $"Edit(/{glob})";
}
