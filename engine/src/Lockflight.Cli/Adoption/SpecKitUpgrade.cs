using System.Text.Json;
using System.Text.Json.Nodes;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Adoption;

/// <summary>
/// Generates stock Spec Kit installs of the installed and the pinned version with <c>uvx</c> in temporary directories, merges the
/// pinned one into the project (<see cref="SpecKitMerge"/>), and installs the Lockflight preset. An installed pre-release that
/// is not published (<c>1.0.5.dev0</c>) is compared with its release (<c>1.0.5</c>): a file that differs from that stock copy
/// counts as customised and is kept, so the nearer base can only keep more, never overwrite more. Without <c>uvx</c>, or when no
/// stock install works, nothing is changed.
/// </summary>
internal static class SpecKitUpgrade
{
    public static async Task<(SpecKitMergeResult? Result, string? Problem, string? ComparedWith)> RunAsync(string root, string? presetDirectory, CancellationToken cancellationToken)
    {
        string? installed = InstalledVersion(root);
        if (installed is null)
        {
            return (null, "the installed Spec Kit version is unknown (.specify/init-options.json has no speckit_version)", null);
        }

        DirectoryInfo work = Directory.CreateTempSubdirectory("lockflight-speckit-");
        try
        {
            string oldStock = Path.Combine(work.FullName, "old");
            string newStock = Path.Combine(work.FullName, "new");
            string? comparedWith = null;
            string? oldProblem = null;
            foreach (string candidate in new[] { installed, ReleaseOf(installed) }.OfType<string>().Distinct(StringComparer.Ordinal))
            {
                if (Directory.Exists(oldStock))
                {
                    Directory.Delete(oldStock, recursive: true);
                }

                oldProblem = await StockAsync(candidate, oldStock, cancellationToken);
                if (oldProblem is null)
                {
                    comparedWith = candidate;
                    break;
                }
            }

            if (comparedWith is null)
            {
                return (null, oldProblem, null);
            }

            if (await StockAsync(SetupInventory.PinnedSpecKitVersion, newStock, cancellationToken) is string newProblem)
            {
                return (null, newProblem, null);
            }

            SpecKitMergeResult result = SpecKitMerge.Merge(root, oldStock, newStock);
            if (presetDirectory is not null)
            {
                Dictionary<string, string> customisedBefore = result.Customised.ToDictionary(file => file, file => Fingerprint(root, file), StringComparer.Ordinal);
                ProcessResult preset = await ProcessRunner.RunAsync(
                    "uvx", ["--from", $"specify-cli=={SetupInventory.PinnedSpecKitVersion}", "specify", "preset", "add", "--dev", presetDirectory], root, cancellationToken);
                if (preset.ExitCode != 0)
                {
                    return (result, $"the Lockflight preset was not installed: {preset.Combined.Trim()}", comparedWith);
                }

                result = result with { ReplacedByPreset = [.. customisedBefore.Where(entry => Fingerprint(root, entry.Key) != entry.Value).Select(entry => entry.Key)] };
            }

            return (result, null, comparedWith);
        }
        catch (ExecutableNotFoundException)
        {
            return (null, "uvx is not installed (https://docs.astral.sh/uv/)", null);
        }
        finally
        {
            work.Delete(recursive: true);
        }
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

    private static async Task<string?> StockAsync(string version, string directory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        await ProcessRunner.RunAsync("git", ["init", "-q"], directory, cancellationToken);
        ProcessResult init = await ProcessRunner.RunAsync(
            "uvx",
            ["--from", $"specify-cli=={version}", "specify", "init", "--here", "--force", "--integration", "claude", "--script", "sh", "--ignore-agent-tools"],
            directory,
            cancellationToken);
        string reason = init.Combined.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault()?.TrimEnd('.') ?? "no output";
        return init.ExitCode == 0 ? null : $"a stock Spec Kit {version} install failed ({reason})";
    }
}
