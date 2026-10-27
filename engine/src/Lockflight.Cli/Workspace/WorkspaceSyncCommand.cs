using Lockflight.Cli.Adoption;
using Lockflight.Cli.Config;
using Lockflight.Cli.Gates.Baselines;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Init;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Projects;

namespace Lockflight.Cli.Workspace;

/// <summary>
/// <c>lockflight workspace sync [--check]</c> (vendored policy): writes into every .NET unit that is its own repository what
/// its CI needs to judge pull requests without the shell — <c>lockflight.unit.yaml</c>, the engine pin, the unit's baselines and
/// the CI workflow (an edited workflow is rewritten) — all derived from the shell's protected policy, so running it never loosens
/// anything.
/// <c>--check</c> changes nothing and fails when a unit has drifted from the shell.
/// </summary>
internal sealed class WorkspaceSyncCommand(CliContext context)
{
    public async Task<ExitCode> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (await CommandOptions.ParseOrReportAsync("workspace sync", args, [], ["--check"], context.Error, cancellationToken) is not CommandOptions options
            || await ManifestRequirement.LoadAsync(context, context.WorkingDirectory, cancellationToken) is not Manifest manifest)
        {
            return ExitCode.Config;
        }

        bool check = options.Flag("--check");
        Unit[] units = [.. Vendored(manifest)];
        if (units.Length == 0)
        {
            await context.Output.WriteLineAsync("No .NET unit is its own repository; nothing to vendor.");
            return ExitCode.Pass;
        }

        string commit = await Git.HeadAsync(manifest.Root, cancellationToken) ?? Git.UnknownCommit;
        List<string> drift = [];
        foreach (Unit unit in units)
        {
            List<string> changes = Sync(manifest, unit, commit, check);
            drift.AddRange(changes.Select(change => $"{unit.Path}: {change}"));
            await context.Output.WriteLineAsync($"{(changes.Count == 0 ? "in sync" : check ? "DRIFT  " : "synced "),-8} {unit.Path}{(changes.Count == 0 ? string.Empty : $" ({string.Join(", ", changes)})")}");
        }

        if (!check && drift.Count > 0)
        {
            await new LedgerWriter(manifest.Root, context.Time).AppendAsync(
                new LedgerEvent(LedgerEventType.ConfigWritten, "workspace:sync", string.Join("; ", drift), null), cancellationToken);
            await context.Output.WriteLineAsync("Commit the synced files in each unit repository; their CI then judges pull requests with this policy.");
        }

        return check && drift.Count > 0 ? ExitCode.Fail : ExitCode.Pass;
    }

    /// <summary>Units that get vendored policy: .NET units that are their own repository (a unit without a .NET build has no gates for its CI yet).</summary>
    public static IEnumerable<Unit> Vendored(Manifest manifest) =>
        manifest.Units.Where(unit => UnitDiscovery.IsRepository(DirectoryOf(manifest, unit)) && Gates.WorkspaceGateRunner.BuildsWithDotnet(manifest, unit));

    /// <summary>What differs between each vendored unit and the shell's policy, without changing anything.</summary>
    public static IReadOnlyList<string> Drift(Manifest manifest) =>
        [.. Vendored(manifest).SelectMany(unit => Sync(manifest, unit, Git.UnknownCommit, check: true).Select(change => $"{unit.Path}: {change}"))];

    private static string DirectoryOf(Manifest manifest, Unit unit) => Path.Combine(manifest.Root, unit.Path.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Brings one unit in line (or, with <paramref name="check"/>, only reports); returns what differed.</summary>
    private static List<string> Sync(Manifest manifest, Unit unit, string commit, bool check)
    {
        string directory = DirectoryOf(manifest, unit);
        List<string> changes = [];

        string policyPath = Path.Combine(directory, Manifest.UnitFileName);
        string? current = File.Exists(policyPath) ? File.ReadAllText(policyPath) : null;
        if (current is null || UnitPolicy.BodyOf(current) != UnitPolicy.Body(manifest, unit))
        {
            changes.Add(Manifest.UnitFileName);
            if (!check)
            {
                File.WriteAllText(policyPath, UnitPolicy.Render(manifest, unit, commit));
            }
        }

        string pinPath = Path.Combine(directory, ToolManifestWriter.RelativePathIn(directory));
        (string pinned, bool pinChanged) = ToolManifestWriter.Pin(File.Exists(pinPath) ? File.ReadAllText(pinPath) : null);
        if (pinChanged)
        {
            changes.Add("engine pin");
            if (!check)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(pinPath)!);
                File.WriteAllText(pinPath, pinned);
            }
        }

        if (SyncBaselines(Path.Combine(manifest.Root, BaselinePaths.Directory, unit.Path), Path.Combine(directory, BaselinePaths.Directory), check) is string baselines)
        {
            changes.Add(baselines);
        }

        // The workflow is generated policy too: an edited one (continue-on-error, a skipped step) is drift, and sync rewrites it.
        string workflow = Path.Combine(directory, Templates.WorkflowPath);
        string[] frameworks = [.. ProjectFiles.TestProjects(directory).SelectMany(project => ProjectFiles.TargetFrameworks(directory, project)).Distinct(StringComparer.OrdinalIgnoreCase)];
        string expected = Templates.UnitWorkflow(DotnetChannels.For(frameworks));
        if (!File.Exists(workflow) || File.ReadAllText(workflow) != expected)
        {
            changes.Add(!File.Exists(workflow) ? "CI workflow" : check ? "CI workflow differs from the generated one" : "CI workflow rewritten: it differed from the generated one");
            if (!check)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(workflow)!);
                File.WriteAllText(workflow, expected);
            }
        }

        string gitIgnore = Path.Combine(directory, ".gitignore");
        (string ignored, bool ignoreChanged) = GitIgnoreUpdater.Update(File.Exists(gitIgnore) ? File.ReadAllText(gitIgnore) : null);
        if (ignoreChanged && !check)
        {
            File.WriteAllText(gitIgnore, ignored);
        }

        return changes;
    }

    /// <summary>
    /// Makes the unit's baselines folder a copy of the shell's baselines for the unit; says what differed, or null. Baselines that
    /// exist only in the unit are named: they are accepted in the shell (<c>baseline … --path &lt;unit&gt;</c>), not in the unit.
    /// </summary>
    private static string? SyncBaselines(string source, string target, bool check)
    {
        Dictionary<string, byte[]> wanted = Files(source);
        Dictionary<string, byte[]> present = Files(target);
        bool same = wanted.Count == present.Count && wanted.All(entry => present.TryGetValue(entry.Key, out byte[]? bytes) && bytes.AsSpan().SequenceEqual(entry.Value));
        if (same)
        {
            return null;
        }

        string[] stale = [.. present.Keys.Where(name => !wanted.ContainsKey(name)).Order(StringComparer.Ordinal)];
        string change = stale.Length == 0 ? "baselines" : $"baselines (only in the unit, {(check ? "would be removed" : "removed")}: {string.Join(", ", stale)})";
        if (check)
        {
            return change;
        }

        foreach (string name in stale)
        {
            File.Delete(Path.Combine(target, name));
        }

        foreach ((string name, byte[] bytes) in wanted)
        {
            string path = Path.Combine(target, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }

        return change;
    }

    private static Dictionary<string, byte[]> Files(string directory) => Directory.Exists(directory)
        ? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(directory, path), File.ReadAllBytes, StringComparer.Ordinal)
        : new Dictionary<string, byte[]>(StringComparer.Ordinal);
}
