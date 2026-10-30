using System.Text.Json;
using System.Text.Json.Nodes;
using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Init;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Doctor;

/// <summary>
/// Readiness of a workspace's units: cloned, no second policy inside a unit, child repositories ignored by the shell,
/// the plugin enabled at user scope (sessions started inside a unit load only user-level plugins), and no SDK pin that
/// keeps <c>dotnet lockflight</c> (the tool pin) from running. The SDK check also applies to a single repository.
/// </summary>
internal static class WorkspaceChecks
{
    private const int MaxListed = 5;

    private const int EngineSdkMajor = 10;

    public static async Task<IReadOnlyList<DoctorCheck>> RunAsync(string root, Manifest? manifest, string? userHome, DateTimeOffset now, CancellationToken cancellationToken)
    {
        List<DoctorCheck> checks = [];
        IReadOnlyList<Unit> units = manifest?.Units ?? [];
        if (units.Count > 0)
        {
            string[] missing = [.. units.Where(unit => !Directory.Exists(UnitDirectory(root, unit))).Select(unit => unit.Path)];
            checks.Add(missing.Length == 0
                ? new("units", CheckStatus.Pass, $"{units.Count} units present", null)
                : new("units", CheckStatus.Warn, $"not cloned yet: {List(missing)}; the guard protects their paths, the gates skip them", "clone each repository into its unit path"));

            string[] policies = [.. units.Where(unit => File.Exists(Path.Combine(UnitDirectory(root, unit), Manifest.FileName)) && !Workspace.UnitPointer.PointsToShell(UnitDirectory(root, unit), unit)).Select(unit => unit.Path)];
            checks.Add(policies.Length == 0
                ? new("unit manifests", CheckStatus.Pass, "one policy for the workspace (no lockflight.yaml inside a unit but a shell: pointer naming that unit)", null)
                : new("unit manifests", CheckStatus.Fail, $"units with their own {Manifest.FileName}: {List(policies)}; a session started there would follow that file instead of the workspace's", $"remove {Manifest.FileName} from those units (one policy per workspace)"));

            checks.Add(await IgnoredChildrenAsync(root, units, cancellationToken));
            IReadOnlyList<string> drift = Workspace.WorkspaceSyncCommand.Drift(manifest!);
            checks.Add(drift.Count == 0
                ? new("unit CI policy", CheckStatus.Pass, "every .NET unit repository carries the shell's current policy", null)
                : new("unit CI policy", CheckStatus.Warn, $"unit repositories behind the shell's policy: {List(drift)}; their CI judges with the old one", $"{EngineCommand.Linked} workspace sync, then commit in each unit"));
            checks.Add(UserScopePlugin(userHome));
            if (StopTimings.Slow(root, now.AddDays(-14)) is { Count: > 0 } slow)
            {
                checks.Add(new("stop time", CheckStatus.Warn,
                    $"slow at Stop over the last 14 days (95th percentile): {List([.. slow.Select(entry => $"{entry.Unit} {entry.P95.TotalSeconds:0} s")])}",
                    "set units[].test.stopFilter to leave slow tests (integration, containers) to the pr profile"));
            }
        }

        string[] pinned = [.. units.Select(unit => UnitDirectory(root, unit)).Prepend(root).Where(directory => Directory.Exists(directory) && PinsOlderSdk(directory))
            .Select(directory => Path.GetRelativePath(root, directory).Replace('\\', '/'))];
        if (pinned.Length > 0)
        {
            checks.Add(new("sdk pin", CheckStatus.Warn,
                $"global.json pins an SDK older than {EngineSdkMajor} in {List(pinned)}: 'dotnet lockflight' cannot run there (the hooks are not affected, they run the linked engine)",
                "run ~/.lockflight/bin/lockflight or .lockflight/bin/lockflight directly, or allow SDK 10 with \"rollForward\": \"latestMajor\""));
        }

        return checks;
    }

    private static string UnitDirectory(string root, Unit unit) => Path.Combine(root, unit.Path.Replace('/', Path.DirectorySeparatorChar));

    private static async Task<DoctorCheck> IgnoredChildrenAsync(string root, IReadOnlyList<Unit> units, CancellationToken cancellationToken)
    {
        List<string> tracked = [];
        foreach (Unit unit in units.Where(unit => Adoption.UnitDiscovery.IsRepository(UnitDirectory(root, unit))))
        {
            ProcessResult ignored = await ProcessRunner.RunAsync("git", ["check-ignore", "-q", unit.Path + "/"], root, cancellationToken);
            if (ignored.ExitCode == 1)
            {
                tracked.Add(unit.Path + "/");
            }
        }

        return tracked.Count == 0
            ? new("child repositories", CheckStatus.Pass, "ignored by the shell's git", null)
            : new("child repositories", CheckStatus.Warn, $"the shell's git does not ignore {List(tracked)}, so it would track their files", $"add {string.Join(" ", tracked)} to .gitignore (init does)");
    }

    private static DoctorCheck UserScopePlugin(string? userHome)
    {
        bool enabled = false;
        string settings = Path.Combine(userHome ?? string.Empty, ".claude", "settings.json");
        try
        {
            enabled = userHome is not null && File.Exists(settings)
                && JsonNode.Parse(File.ReadAllText(settings))?["enabledPlugins"]?[Templates.PluginId]?.GetValueKind() == JsonValueKind.True;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or IOException)
        {
            // An unreadable user settings file is reported as "not enabled".
        }

        return enabled
            ? new("plugin scope", CheckStatus.Pass, "enabled at user scope: the guard also runs in sessions started inside a unit", null)
            : new("plugin scope", CheckStatus.Warn, "not enabled at user scope: a Claude Code session started inside a unit loads no project settings, so neither the guard nor your own hooks run there",
                $"claude plugin install {Templates.PluginId} --scope user (and start sessions at the shell root)");
    }

    /// <summary>A global.json whose SDK version is older than 10 and whose roll-forward cannot reach a newer major.</summary>
    private static bool PinsOlderSdk(string directory)
    {
        string path = Path.Combine(directory, "global.json");
        try
        {
            if (!File.Exists(path) || JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })?["sdk"] is not JsonObject sdk)
            {
                return false;
            }

            string version = sdk["version"]?.GetValue<string>() ?? string.Empty;
            string rollForward = sdk["rollForward"]?.GetValue<string>() ?? "latestPatch";
            return int.TryParse(version.Split('.')[0], out int major) && major < EngineSdkMajor
                && rollForward is not ("latestMajor" or "major");
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or IOException)
        {
            return false;
        }
    }

    private static string List(IReadOnlyList<string> items) =>
        string.Join(", ", items.Take(MaxListed)) + (items.Count > MaxListed ? $" and {items.Count - MaxListed} more" : string.Empty);
}
