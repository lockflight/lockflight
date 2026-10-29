using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Lockflight.Cli.Config;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Gates;

/// <summary>
/// A fingerprint of everything a stop-gate run depends on: the engine build, the effective gate settings and — in every working tree
/// of the project (a workspace's child repositories too) — HEAD, all uncommitted changes and the contents of new files. Equal fingerprints mean the gate would judge the same code the same way, so
/// a cached PASS stands. Null outside a git repository or when any git query fails — then nothing is cached.
/// </summary>
internal static class InputsHash
{
    /// <summary>The engine build, metadata included: a rebuilt engine may judge differently, so it never reuses an older PASS.</summary>
    private static readonly string EngineBuild =
        typeof(InputsHash).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? ToolPin.RunningVersion;

    public static async Task<string?> ComputeAsync(string root, CancellationToken cancellationToken)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, EngineBuild);
        Append(hash, EffectiveSettings(root));

        // Every working tree of a workspace: a child repository's edits are invisible to the shell's git, and a PASS
        // cached before them must not stand.
        IReadOnlyList<RepoRoot> repos = WorkspaceRepos.Of(root);
        foreach (RepoRoot repo in repos)
        {
            if (await Git.HeadAsync(repo.Directory, cancellationToken) is not string head)
            {
                return null;
            }

            ProcessResult diff = await ProcessRunner.RunAsync("git", ["diff", "HEAD", "--binary", "--no-ext-diff", "--no-color"], repo.Directory, cancellationToken);
            if (diff.ExitCode != 0 || await Git.UntrackedFilesAsync(repo.Directory, cancellationToken) is not IReadOnlyList<string> untracked)
            {
                return null;
            }

            Append(hash, repo.Prefix);
            Append(hash, head);
            Append(hash, diff.Output);
            foreach (string file in untracked.Where(file => repo.Prefix.Length > 0 || !WorkspaceRepos.InChildRepository(repos, file)).Order(StringComparer.Ordinal))
            {
                Append(hash, file);
                string path = Path.Combine(repo.Directory, file);
                if (File.Exists(path))
                {
                    hash.AppendData(await File.ReadAllBytesAsync(path, cancellationToken));
                }
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>The engine build and the effective settings, for keys that cover one unit (<see cref="UnitState"/>).</summary>
    public static string EngineAndSettings(string root) => $"{EngineBuild}|{EffectiveSettings(root)}";

    /// <summary>The cache key of the unit steps one unit runs in the stop profile (<see cref="GateProfiles"/>).</summary>
    public static string UnitStepsKey(Unit unit) => StepsKey(new CanonicalKey().Add("unit", unit.Path), GateProfiles.For(GateProfile.Stop).PerUnit);

    /// <summary>
    /// The settings the stop gate would run with, as loaded, field by field, and the cache keys of its steps (a manifest that
    /// does not load yields its absence). A field added to the stop, gate, coverage or unit settings is added here too.
    /// </summary>
    private static string EffectiveSettings(string root)
    {
        if (ManifestLocator.Discover(root).Manifest is not Manifest manifest)
        {
            return "no manifest";
        }

        GateSettings gates = manifest.Gates;
        CanonicalKey key = new CanonicalKey()
            .Add("stop.gate", manifest.StopGate)
            .Add("stop.target", manifest.StopTarget)
            .Add("gates.timeoutSeconds", gates.TimeoutSeconds)
            .Add("gates.build", gates.BuildFile)
            .Add("gates.test.framework", gates.TestFramework)
            .Add("gates.coverage.collector", (int)gates.Coverage.Collector)
            .Add("gates.coverage.patchThreshold", gates.Coverage.PatchThreshold)
            .Add("gates.coverage.minChangedLines", gates.Coverage.MinChangedLines)
            .Add("gates.quality.suppressions", (int)gates.Suppressions);
        foreach (Unit unit in manifest.Units)
        {
            key.Add("unit.path", unit.Path)
                .Add("unit.build", unit.Build)
                .Add("unit.kind", (int)unit.Kind)
                .Add("unit.test.framework", unit.TestFramework)
                .Add("unit.test.stopFilter", unit.StopFilter);
        }

        return StepsKey(key, GateProfiles.For(GateProfile.Stop).InOrder);
    }

    private static string StepsKey(CanonicalKey key, IReadOnlyList<IGateStep> steps)
    {
        foreach (IGateStep step in steps)
        {
            key.Add("step", step.CacheKey);
        }

        return key.ToString();
    }

    /// <summary>Appends a value with a separator, so adjacent values cannot run together into the same bytes.</summary>
    private static void Append(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0]);
    }
}
