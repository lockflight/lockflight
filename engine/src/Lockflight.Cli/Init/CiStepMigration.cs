namespace Lockflight.Cli.Init;

/// <summary>
/// Moves a generated CI file to the engine lock by editing only the lines Lockflight wrote, never the whole file: the engine
/// install step (between its markers, or as Lockflight 0.2 wrote it), and in the base-branch policy restore the file list (now
/// with <c>.lockflight/engine.lock</c>) and the baselines lines (now the policy directories, the vendored install script too).
/// Each edited block must be found exactly once with the expected text; otherwise the file is left alone and the caller writes
/// the new step for a person to merge.
/// </summary>
internal static class CiStepMigration
{
    /// <summary>The CI files <c>init</c> writes, by platform, plus the Bitbucket fragment a person merges by hand.</summary>
    public static IReadOnlyList<string> KnownFiles { get; } =
        [Templates.WorkflowPath, AzurePipelinesTemplate.Path, BitbucketPipelinesTemplate.Path, BitbucketPipelinesTemplate.FragmentPath, GitLabCiTemplate.Path];

    /// <summary>The policy file list before the engine lock.</summary>
    private const string LegacyPolicyFiles = "dotnet-tools.json .config/dotnet-tools.json lockflight.yaml .gitleaks.toml .gitleaksignore nuget.config NuGet.config";

    private static readonly string[] LegacyBaselines =
    [
        "rm -rf .lockflight/baselines",
        "if git cat-file -e \"$BASE:.lockflight/baselines\" 2>/dev/null; then git checkout \"$BASE\" -- .lockflight/baselines; fi",
    ];

    private static readonly string[] Directories =
    [
        $"for directory in {CiShellScripts.PolicyDirectories}; do",
        "  rm -rf \"$directory\"",
        "  if git cat-file -e \"$BASE:$directory\" 2>/dev/null; then git checkout \"$BASE\" -- \"$directory\"; fi",
        "done",
    ];

    /// <summary>The migrated text; null when the file must be left alone. The file's own text when it is current already.</summary>
    public static string? Migrate(string text)
    {
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        List<string> lines = [.. text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')];
        if (!ReplaceInstallStep(lines) || !ReplaceRestore(lines))
        {
            return null;
        }

        return string.Join(newline, lines);
    }

    private static bool ReplaceInstallStep(List<string> lines)
    {
        string[] install = CiShellScripts.InstallEngine.Split('\n');
        int[] begins = [.. Indexes(lines, line => line.Trim() == CiShellScripts.InstallBeginMarker)];
        int[] ends = [.. Indexes(lines, line => line.Trim() == CiShellScripts.InstallEndMarker)];
        if (begins.Length == 1 && ends.Length == 1 && ends[0] > begins[0])
        {
            string indent = Indent(lines[begins[0]]);
            lines.RemoveRange(begins[0], ends[0] - begins[0] + 1);
            lines.InsertRange(begins[0], install.Select(line => line.Length == 0 ? line : indent + line));
            return true;
        }

        if (begins.Length != 0 || ends.Length != 0)
        {
            return false;
        }

        foreach ((string legacy, string[] prefix) in new[] { (CiShellScripts.LegacyInstallEngine, Array.Empty<string>()), (Templates.LegacyGitHubInstallEngine, ["LOCKFLIGHT_TEMP=\"$RUNNER_TEMP\""]) })
        {
            if (Find(lines, legacy.Split('\n')) is (int start, string indent))
            {
                lines.RemoveRange(start, legacy.Split('\n').Length);
                lines.InsertRange(start, prefix.Concat(install).Select(line => line.Length == 0 ? line : indent + line));
                return true;
            }
        }

        return false;
    }

    private static bool ReplaceRestore(List<string> lines)
    {
        int[] current = [.. Indexes(lines, line => line.Trim() == $"for file in {CiShellScripts.PolicyFiles}; do")];
        if (current.Length == 1)
        {
            return true;
        }

        int[] legacy = [.. Indexes(lines, line => line.Trim() == $"for file in {LegacyPolicyFiles}; do")];
        if (legacy.Length != 1 || Find(lines, LegacyBaselines) is not (int start, string indent))
        {
            return false;
        }

        lines[legacy[0]] = Indent(lines[legacy[0]]) + $"for file in {CiShellScripts.PolicyFiles}; do";
        lines.RemoveRange(start, LegacyBaselines.Length);
        lines.InsertRange(start, Directories.Select(line => indent + line));
        return true;
    }

    /// <summary>Where <paramref name="block"/> occurs exactly once as consecutive lines with one indentation; null otherwise.</summary>
    private static (int Start, string Indent)? Find(List<string> lines, string[] block)
    {
        List<(int Start, string Indent)> found = [];
        for (int start = 0; start + block.Length <= lines.Count; start++)
        {
            string indent = Indent(lines[start]);
            if (block.Select((line, offset) => lines[start + offset] == (line.Length == 0 ? line : indent + line)).All(same => same))
            {
                found.Add((start, indent));
            }
        }

        return found.Count == 1 ? found[0] : null;
    }

    private static IEnumerable<int> Indexes(List<string> lines, Func<string, bool> predicate) =>
        Enumerable.Range(0, lines.Count).Where(index => predicate(lines[index]));

    private static string Indent(string line) => line[..(line.Length - line.TrimStart().Length)];
}
