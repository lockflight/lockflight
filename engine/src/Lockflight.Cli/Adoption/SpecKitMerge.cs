using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lockflight.Cli.Adoption;

/// <summary>
/// Upgrades the Spec Kit files of a project from two stock installs generated in temporary directories — the installed version
/// and the pinned one — without overwriting the team's work: a file identical to the old stock is replaced by the new
/// stock, a missing file is added, and a file that differs from the old stock (the constitution, an edited template or skill) is
/// kept and reported. Spec Kit's own bookkeeping (install options, integration manifests, the workflow registry) differs on every
/// install, so it always takes the new version. Files the team added and files the new version dropped are left alone, and so is
/// every file that is a symbolic link or lies in a linked folder: the backup does not follow links, so undo could not restore it.
/// </summary>
internal static class SpecKitMerge
{
    private static readonly string[] Generated =
    [
        ".specify/init-options.json",
        ".specify/integration.json",
    ];

    private const string WorkflowRegistry = ".specify/workflows/workflow-registry.json";

    public static SpecKitMergeResult Merge(string root, string oldStock, string newStock)
    {
        List<string> upgraded = [];
        List<string> added = [];
        List<string> customised = [];
        List<string> linked = [];
        foreach (string relative in Files(newStock))
        {
            if (SymbolicLinks.OnPath(root, relative) is not null)
            {
                linked.Add(relative);
                continue;
            }

            string target = Path.Combine(root, relative);
            byte[] fresh = File.ReadAllBytes(Path.Combine(newStock, relative));
            if (!File.Exists(target))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, fresh);
                added.Add(relative);
                continue;
            }

            byte[] current = File.ReadAllBytes(target);
            if (current.AsSpan().SequenceEqual(fresh))
            {
                continue;
            }

            string old = Path.Combine(oldStock, relative);
            if (IsGenerated(relative) || (relative == WorkflowRegistry && File.Exists(old) && OnlyStockWorkflows(current, File.ReadAllBytes(old)))
                || (File.Exists(old) && current.AsSpan().SequenceEqual(File.ReadAllBytes(old))))
            {
                File.WriteAllBytes(target, fresh);
                upgraded.Add(relative);
            }
            else
            {
                customised.Add(relative);
            }
        }

        return new SpecKitMergeResult(upgraded, added, customised) { Linked = linked };
    }

    /// <summary>
    /// The workflow registry carries install timestamps, so it differs on every install; it is the team's own only when it
    /// registers workflows the stock install does not.
    /// </summary>
    private static bool OnlyStockWorkflows(byte[] current, byte[] stock)
    {
        try
        {
            HashSet<string> stockIds = [.. JsonNode.Parse(stock)?["workflows"]?.AsObject().Select(entry => entry.Key) ?? []];
            return JsonNode.Parse(current)?["workflows"]?.AsObject().All(entry => stockIds.Contains(entry.Key)) ?? false;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsGenerated(string relative) =>
        Generated.Contains(relative, StringComparer.Ordinal)
        || (relative.StartsWith(".specify/integrations/", StringComparison.Ordinal) && relative.EndsWith(".manifest.json", StringComparison.Ordinal));

    /// <summary>Spec Kit's own files in a stock install: <c>.specify/</c> and the <c>speckit-*</c> skills, '/'-separated.</summary>
    private static IEnumerable<string> Files(string stock) =>
        Directory.EnumerateFiles(stock, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(stock, path).Replace('\\', '/'))
            .Where(relative => relative.StartsWith(".specify/", StringComparison.Ordinal) || relative.StartsWith(".claude/skills/speckit-", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);
}
