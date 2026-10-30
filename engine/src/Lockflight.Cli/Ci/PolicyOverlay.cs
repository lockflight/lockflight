using System.Text.RegularExpressions;
using Lockflight.Cli.Init;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Lays the base policy over a tree the judge builds, as the CI template does over the checkout: the single policy files, the
/// accepted baselines and the code-style and analyzer config at any depth, each from the policy commit, or removed where the policy
/// commit has none. A tree is then built under the rules it will be judged by after the merge, so a red commit cannot loosen
/// analyzer severity to get past a compile error.
/// </summary>
internal static partial class PolicyOverlay
{
    private const string BaselinesDirectory = ".lockflight/baselines";

    public static async Task<string?> ApplyAsync(string repositoryRoot, string tree, string treeCommit, string policyCommit, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        IReadOnlyList<string>? policyFiles = await JudgeGit.TreeFilesAsync(repositoryRoot, policyCommit, cancellationToken);
        IReadOnlyList<string>? treeFiles = await JudgeGit.TreeFilesAsync(repositoryRoot, treeCommit, cancellationToken);
        if (policyFiles is null || treeFiles is null)
        {
            return $"git could not list the files of {CommitIds.Short(policyCommit)} or {CommitIds.Short(treeCommit)}";
        }

        HashSet<string> atPolicy = [.. policyFiles];
        string[] single = CiShellScripts.PolicyFiles.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string[] style = [.. policyFiles.Concat(treeFiles).Where(path => StyleFile().IsMatch(path)).Distinct(StringComparer.Ordinal)];
        string baselines = Path.Combine(tree, BaselinesDirectory.Replace('/', Path.DirectorySeparatorChar));
        if (Directory.Exists(baselines))
        {
            Directory.Delete(baselines, recursive: true);
        }

        string[] baselineFiles = [.. policyFiles.Where(path => path.StartsWith(BaselinesDirectory + "/", StringComparison.Ordinal))];
        string[] overlaid = [.. single.Concat(style).Concat(baselineFiles).Distinct(StringComparer.Ordinal)];

        // First every file the base does not have under that exact name goes, case variants included (NuGet reads NuGet.Config
        // as well as nuget.config); then the base's files are written. On a case-insensitive file system a variant and the
        // base's file are one file, which the second pass writes back with the base's content.
        foreach (string path in treeFiles.Concat(overlaid).Distinct(StringComparer.Ordinal).Where(path => !atPolicy.Contains(path) && overlaid.Any(policy => policy.Equals(path, StringComparison.OrdinalIgnoreCase))))
        {
            string target = Path.Combine(tree, path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(target))
            {
                File.Delete(target);
            }
        }

        foreach (string path in overlaid.Where(atPolicy.Contains))
        {
            string target = Path.Combine(tree, path.Replace('/', Path.DirectorySeparatorChar));
            ProcessResult blob = await ProcessRunner.RunAsync("git", ["cat-file", "blob", $"{policyCommit}:{path}"], repositoryRoot, timeLimit, cancellationToken);
            if (blob.ExitCode != 0)
            {
                return $"git could not read {path} at {CommitIds.Short(policyCommit)}";
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllTextAsync(target, blob.Output, cancellationToken);
        }

        return null;
    }

    [GeneratedRegex(@"(^|/)(\.editorconfig|\.globalconfig|[^/]*\.globalconfig|[^/]*\.ruleset|stylecop\.json|BannedSymbols\.txt|CodeMetricsConfig\.txt)$")]
    private static partial Regex StyleFile();
}
