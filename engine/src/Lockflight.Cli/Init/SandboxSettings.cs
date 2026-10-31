using System.Text.Json;
using System.Text.Json.Nodes;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Init;

/// <summary>
/// The Claude Code sandbox lists Lockflight writes into a project's <c>.claude/settings.json</c>: <c>sandbox.filesystem.denyWrite</c>
/// for the protected paths, so that while the sandbox is on the operating system refuses a shell command's write to them (macOS
/// Seatbelt, Linux and WSL2 bubblewrap; native Windows has no sandbox), and <c>sandbox.filesystem.allowWrite</c> for the .NET
/// caches outside the project, so builds keep working inside it. The sandbox itself is not switched on here: that is the
/// developer's or the organisation's choice (<c>/sandbox</c>, <c>sandbox.enabled</c>, managed settings).
/// </summary>
internal static class SandboxSettings
{
    public const string SandboxKey = "sandbox";
    public const string FilesystemKey = "filesystem";
    public const string DenyWriteKey = "denyWrite";
    public const string AllowWriteKey = "allowWrite";
    public const string EnabledKey = "enabled";

    private const string TreeSuffix = "/**";
    private const string ProjectRelative = "./";
    private const string LockflightTree = ".lockflight/**";
    private const string LinkedEngine = ".lockflight/bin";
    private const string Baselines = ".lockflight/baselines";
    private const string EngineLock = ".lockflight/engine.lock";
    private const string VendoredScripts = ".lockflight/ci";

    /// <summary>
    /// Where <c>dotnet</c> writes outside the project during a restore and build: the global packages folder, NuGet's HTTP and
    /// plugin caches, and the SDK's first-run and tool files. Sandboxed commands may write only the project and the temp directory.
    /// </summary>
    public static IReadOnlyList<string> DotnetCaches { get; } = ["~/.nuget/packages", "~/.local/share/NuGet", "~/.dotnet"];

    private const string AgentWorktrees = ".claude/worktrees";

    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase) { ".git", "bin", "obj", "node_modules", ".lockflight" };

    /// <summary>
    /// The protected paths as sandbox entries (<c>./</c> is the project root in project settings), for what exists now: on Linux
    /// and WSL2 the sandbox holds a denial on a missing file by placing an empty read-only file there while a command runs, and
    /// an empty <c>global.json</c> or <c>Directory.Build.props</c> would break the build. A pattern such as
    /// <c>**/Directory.Build.props</c> is written as each file it matches, because Linux supports no other wildcard in these lists.
    /// Of <c>.lockflight/</c> only the linked engine, the baselines, the engine lock and the vendored install script are listed
    /// (the last two are written by <c>init</c> and <c>update</c> only): evidence, the ledger and the Stop cache are written by the
    /// commands the agent runs (gate and test runs). Run <c>init</c> again to add files created since.
    /// </summary>
    public static IReadOnlyList<string> DenyWrite(string root, IEnumerable<string> protectedGlobs)
    {
        // The settings file is being written as this list is, and the engine is linked after it.
        SortedSet<string> paths = new(StringComparer.Ordinal) { LinkedEngine, ClaudeSettingsMerger.RelativePath };
        List<string> patterns = [];
        foreach (string glob in protectedGlobs.Select(glob => glob.Replace('\\', '/').TrimStart('/')))
        {
            if (glob.EndsWith(LockflightTree, StringComparison.Ordinal))
            {
                string unit = glob[..^LockflightTree.Length];
                paths.UnionWith(new[] { unit + LinkedEngine, unit + Baselines, unit + EngineLock, unit + VendoredScripts }.Where(path => Exists(root, path) || path == LinkedEngine));
            }
            else if (glob.EndsWith(TreeSuffix, StringComparison.Ordinal) && !PathWildcards.HasWildcard(glob[..^TreeSuffix.Length]))
            {
                AddIfPresent(root, glob[..^TreeSuffix.Length], paths);
            }
            else if (PathWildcards.HasWildcard(glob))
            {
                patterns.Add(glob);
            }
            else
            {
                AddIfPresent(root, glob, paths);
            }
        }

        if (patterns.Count > 0)
        {
            paths.UnionWith(FilesMatching(root, patterns));
        }

        return [.. paths.Select(path => ProjectRelative + path)];
    }

    /// <summary>Why the sandbox lists cannot be merged into: a key on the way is not an object, or a list is not a list of strings.</summary>
    public static string? Problem(JsonObject settings)
    {
        if (settings[SandboxKey] is null)
        {
            return null;
        }

        if (settings[SandboxKey] is not JsonObject sandbox)
        {
            return $"has a \"{SandboxKey}\" that is not an object";
        }

        if (sandbox[FilesystemKey] is null)
        {
            return null;
        }

        if (sandbox[FilesystemKey] is not JsonObject filesystem)
        {
            return $"has a \"{SandboxKey}.{FilesystemKey}\" that is not an object";
        }

        return new[] { DenyWriteKey, AllowWriteKey }.FirstOrDefault(key => !IsStringList(filesystem[key])) is string list
            ? $"has a \"{SandboxKey}.{FilesystemKey}.{list}\" that is not a list of paths"
            : null;
    }

    /// <summary>Adds the entries that are missing to both lists; nothing is removed or reordered. True when something was added.</summary>
    public static bool Merge(JsonObject settings, IReadOnlyList<string> denyWrite)
    {
        JsonObject filesystem = ObjectAt(ObjectAt(settings, SandboxKey), FilesystemKey);
        bool deniedAdded = Append(ArrayAt(filesystem, DenyWriteKey), denyWrite);
        bool allowedAdded = Append(ArrayAt(filesystem, AllowWriteKey), DotnetCaches);
        return deniedAdded || allowedAdded;
    }

    /// <summary>The entries of a settings list; empty when it is missing or not a list of strings.</summary>
    public static IReadOnlyList<string> Entries(JsonNode? settings, string key) =>
        settings?[SandboxKey]?[FilesystemKey]?[key] is JsonArray list && IsStringList(list) ? [.. list.Select(item => item!.GetValue<string>())] : [];

    private static bool IsStringList(JsonNode? node) =>
        node is null || (node is JsonArray list && list.All(item => item?.GetValueKind() == JsonValueKind.String));

    private static bool Append(JsonArray list, IEnumerable<string> entries)
    {
        HashSet<string> present = [.. list.Select(item => item!.GetValue<string>())];
        bool added = false;
        foreach (string entry in entries.Where(present.Add))
        {
            list.Add((JsonNode)JsonValue.Create(entry));
            added = true;
        }

        return added;
    }

    private static void AddIfPresent(string root, string relativePath, SortedSet<string> paths)
    {
        if (Exists(root, relativePath))
        {
            paths.Add(relativePath);
        }
    }

    private static bool Exists(string root, string relativePath) =>
        File.Exists(Path.Combine(root, relativePath)) || Directory.Exists(Path.Combine(root, relativePath));

    /// <summary>The files under the root that match one of the patterns, '/'-separated; build output and git internals are skipped.</summary>
    private static IEnumerable<string> FilesMatching(string root, IReadOnlyList<string> patterns)
    {
        Stack<string> pending = new([root]);
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            string[] children;
            string[] files;
            try
            {
                children = [.. Directory.EnumerateDirectories(directory)];
                files = [.. Directory.EnumerateFiles(directory)];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string file in files)
            {
                string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (patterns.Any(pattern => PathRules.Matches(pattern, relative)))
                {
                    yield return relative;
                }
            }

            foreach (string child in children.Where(child => !Skipped(root, child)))
            {
                pending.Push(child);
            }
        }
    }

    /// <summary>Build output, git internals, agent worktrees (other checkouts) and symbolic links, which may lead out of the project.</summary>
    private static bool Skipped(string root, string directory) =>
        SkippedDirectories.Contains(Path.GetFileName(directory))
        || Path.GetRelativePath(root, directory).Replace('\\', '/') == AgentWorktrees
        || new DirectoryInfo(directory).LinkTarget is not null;

    private static JsonObject ObjectAt(JsonObject parent, string key)
    {
        if (parent[key] is JsonObject existing)
        {
            return existing;
        }

        JsonObject created = [];
        parent[key] = created;
        return created;
    }

    private static JsonArray ArrayAt(JsonObject parent, string key)
    {
        if (parent[key] is JsonArray existing)
        {
            return existing;
        }

        JsonArray created = [];
        parent[key] = created;
        return created;
    }
}
