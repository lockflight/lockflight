using System.Text.Json;
using System.Text.Json.Nodes;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Adoption;

/// <summary>
/// Generates stock Spec Kit installs of the installed and the pinned version with <c>uvx</c> in temporary directories, merges the
/// pinned one into the project (<see cref="SpecKitMerge"/>), and installs the Lockflight preset. An installed pre-release that
/// is not published (<c>1.0.5.dev0</c>) is compared with its release (<c>1.0.5</c>): a file that differs from that stock copy
/// counts as customised and is kept, so the nearer base can only keep more, never overwrite more. Each stock install comes from
/// PyPI, or from the version's GitHub tag when PyPI lacks it (<see cref="SpecKitSources"/>); the result says which tag was used.
/// A Spec Kit newer than the pinned version is never merged (that would be a downgrade): only the preset is installed. Without
/// <c>uvx</c>, or when no stock install works, nothing is changed.
/// </summary>
internal static class SpecKitUpgrade
{
    private const string UvxMissing = "uvx is not installed (https://docs.astral.sh/uv/)";

    public static async Task<(SpecKitMergeResult? Result, string? Problem, string? ComparedWith, string? Sources)> RunAsync(string root, string? presetDirectory, CancellationToken cancellationToken)
    {
        string? installed = InstalledVersion(root);
        if (installed is null)
        {
            return (null, "the installed Spec Kit version is unknown (.specify/init-options.json has no speckit_version)", null, null);
        }

        DirectoryInfo work = Directory.CreateTempSubdirectory("lockflight-speckit-");
        try
        {
            string oldStock = Path.Combine(work.FullName, "old");
            string newStock = Path.Combine(work.FullName, "new");
            string? comparedWith = null;
            string? oldProblem = null;
            SpecKitSource? oldSource = null;
            foreach (string candidate in new[] { installed, ReleaseOf(installed) }.OfType<string>().Distinct(StringComparer.Ordinal))
            {
                (oldProblem, oldSource) = await StockAsync(candidate, oldStock, cancellationToken);
                if (oldProblem is null)
                {
                    comparedWith = candidate;
                    break;
                }
            }

            if (comparedWith is null)
            {
                return (null, oldProblem, null, null);
            }

            (string? newProblem, SpecKitSource? newSource) = await StockAsync(SetupInventory.PinnedSpecKitVersion, newStock, cancellationToken);
            if (newProblem is not null)
            {
                return (null, newProblem, null, null);
            }

            string? sources = Describe([(comparedWith, oldSource), (SetupInventory.PinnedSpecKitVersion, newSource)]);
            SpecKitMergeResult result = SpecKitMerge.Merge(root, oldStock, newStock);
            if (presetDirectory is not null)
            {
                Dictionary<string, string> customisedBefore = result.Customised.ToDictionary(file => file, file => Fingerprint(root, file), StringComparer.Ordinal);
                if (await AddPresetAsync(root, presetDirectory, SetupInventory.PinnedSpecKitVersion, cancellationToken) is (string presetProblem, _))
                {
                    return (result, presetProblem, comparedWith, sources);
                }

                result = result with { ReplacedByPreset = [.. customisedBefore.Where(entry => Fingerprint(root, entry.Key) != entry.Value).Select(entry => entry.Key)] };
            }

            return (result, null, comparedWith, sources);
        }
        catch (ExecutableNotFoundException)
        {
            return (null, UvxMissing, null, null);
        }
        finally
        {
            work.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Installs only the Lockflight preset, with the project's own Spec Kit version, into a Spec Kit newer than the pinned one.
    /// Returns the problem (null when it worked) and where that Spec Kit came from when it was not PyPI.
    /// </summary>
    public static async Task<(string? Problem, string? Source)> AddPresetOnlyAsync(string root, string presetDirectory, CancellationToken cancellationToken)
    {
        if (InstalledVersion(root) is not string installed)
        {
            return ("the installed Spec Kit version is unknown (.specify/init-options.json has no speckit_version)", null);
        }

        try
        {
            (string? problem, SpecKitSource? source) = await AddPresetAsync(root, presetDirectory, installed, cancellationToken);
            return (problem, Describe([(installed, source)]));
        }
        catch (ExecutableNotFoundException)
        {
            return (UvxMissing, null);
        }
    }

    /// <summary>
    /// True when the installed version's release is newer than the pinned one (<c>1.2.3</c>, <c>1.2.0.dev0</c> against
    /// <c>1.1.3</c>); false for an older, equal or unreadable version.
    /// </summary>
    public static bool IsNewerThanPinned(string version) =>
        Release(version) is Version installed && Release(SetupInventory.PinnedSpecKitVersion) is Version pinned && installed > pinned;

    private static Version? Release(string version) =>
        System.Text.RegularExpressions.Regex.Match(version, @"^\d+\.\d+\.\d+") is { Success: true } match && Version.TryParse(match.Value, out Version? parsed) ? parsed : null;

    /// <summary>Runs <c>specify preset add</c> with the first source of the version that works.</summary>
    private static async Task<(string? Problem, SpecKitSource? Source)> AddPresetAsync(string root, string presetDirectory, string version, CancellationToken cancellationToken)
    {
        string problem = $"Spec Kit {version} is not a version uvx can install";
        foreach (SpecKitSource source in SpecKitSources.For(version))
        {
            ProcessResult preset = await ProcessRunner.RunAsync("uvx", ["--from", source.From, "specify", "preset", "add", "--dev", presetDirectory], root, cancellationToken);
            if (preset.ExitCode == 0)
            {
                return (null, source);
            }

            problem = $"the Lockflight preset was not installed: {preset.Combined.Trim()}";
        }

        return (problem, null);
    }

    /// <summary>The stock installs that did not come from PyPI, such as <c>1.1.0 from GitHub tag v1.1.0</c>; null when all did.</summary>
    private static string? Describe(IEnumerable<(string Version, SpecKitSource? Source)> installs)
    {
        string[] notes = [.. installs.Where(install => install.Source is { IsPackageIndex: false }).Select(install => $"{install.Version} from {install.Source!.Name} (not on PyPI)")];
        return notes.Length == 0 ? null : string.Join(", ", notes);
    }

    private static string Fingerprint(string root, string relative)
    {
        string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))) : "absent";
    }

    /// <summary>The release a pre-release or local version belongs to (<c>1.0.5.dev0</c> → <c>1.0.5</c>); null for a release.</summary>
    public static string? ReleaseOf(string version)
    {
        System.Text.RegularExpressions.Match release = System.Text.RegularExpressions.Regex.Match(version, @"^\d+\.\d+\.\d+");
        return release.Success && release.Value != version ? release.Value : null;
    }

    public static string? InstalledVersion(string root)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(Path.Combine(root, ".specify", "init-options.json")))?["speckit_version"]?.GetValue<string>();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// A stock install of the version in a fresh directory, from PyPI or else the version's GitHub tag. Returns the problem of the
    /// last source tried (null when one worked) and the source that worked.
    /// </summary>
    private static async Task<(string? Problem, SpecKitSource? Source)> StockAsync(string version, string directory, CancellationToken cancellationToken)
    {
        string problem = $"a stock Spec Kit {version} install failed (not a version uvx can install)";
        List<string> tried = [];
        foreach (SpecKitSource source in SpecKitSources.For(version))
        {
            tried.Add(source.Name);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }

            Directory.CreateDirectory(directory);
            await ProcessRunner.RunAsync("git", ["init", "-q"], directory, cancellationToken);
            ProcessResult init = await ProcessRunner.RunAsync(
                "uvx",
                ["--from", source.From, "specify", "init", "--here", "--force", "--integration", "claude", "--script", "sh", "--ignore-agent-tools"],
                directory,
                cancellationToken);
            if (init.ExitCode == 0)
            {
                return (null, source);
            }

            string reason = init.Combined.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault()?.TrimEnd('.') ?? "no output";
            problem = $"a stock Spec Kit {version} install failed from {string.Join(" and ", tried)} ({reason})";
        }

        return (problem, null);
    }
}
