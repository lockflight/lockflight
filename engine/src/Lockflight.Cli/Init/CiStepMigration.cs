namespace Lockflight.Cli.Init;

/// <summary>
/// Moves a generated CI file to the engine lock by editing only the lines Lockflight wrote, never the whole file: the engine
/// install step (between its markers, or as Lockflight 0.2 wrote it), and in the base-branch policy restore the file list (now
/// with <c>.lockflight/engine.lock</c>) and the baselines lines (now the policy directories, the vendored install script too),
/// and on Azure and GitLab the pull request's base (now the target branch's tip, not the merge base or the diff base).
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
        "# Accepted baselines (known-failing tests, warnings, advisories) are policy too: a PR cannot accept its own failures.",
        "rm -rf .lockflight/baselines",
        "if git cat-file -e \"$BASE:.lockflight/baselines\" 2>/dev/null; then git checkout \"$BASE\" -- .lockflight/baselines; fi",
    ];

    private const string LegacyGitHubStyleLoop = "{ git ls-tree -r --name-only \"$BASE\"; git ls-files; } | grep -E \"$style\" | sort -u | while IFS= read -r file; do";
    private const string StyleLoop = "{ git ls-tree -r --name-only \"$BASE\"; git ls-files; } | { grep -E \"$style\" || true; } | sort -u | while IFS= read -r file; do";

    private static readonly string[] Directories =
    [
        "# Accepted baselines (known-failing tests, warnings, advisories) are policy too: a PR cannot accept its own failures. So is",
        "# the vendored engine install script: CI runs the base commit's copy, checked against the base commit's lock.",
        $"for directory in {CiShellScripts.PolicyDirectories}; do",
        "  rm -rf \"$directory\"",
        "  if git cat-file -e \"$BASE:$directory\" 2>/dev/null; then git checkout \"$BASE\" -- \"$directory\"; fi",
        "done",
    ];

    /// <summary>
    /// The Azure and GitLab lines that took the policy from the merge base or the merge request's diff base, and the lines that take
    /// it from the target branch's tip; each is replaced only when found exactly once.
    /// </summary>
    private static readonly (string[] Legacy, string[] Current)[] BaseBlocks =
    [
        (
            ["merge_request_event) PULL_REQUEST=1; BASE=\"$CI_MERGE_REQUEST_DIFF_BASE_SHA\" ;;"],
            [
                "merge_request_event)",
                "  # Policy comes from the target branch's tip, not the merge request's diff base: a branch cut from an old commit must",
                "  # not bring an older engine pin, baselines or analyzer config. The gates work out the merge base themselves.",
                "  PULL_REQUEST=1",
                "  git fetch --quiet origin \"+refs/heads/$CI_MERGE_REQUEST_TARGET_BRANCH_NAME:refs/remotes/origin/$CI_MERGE_REQUEST_TARGET_BRANCH_NAME\"",
                "  BASE=$(git rev-parse \"origin/$CI_MERGE_REQUEST_TARGET_BRANCH_NAME\") ;;",
            ]),
        (
            ["# The engine version and policy come from the merge request's diff base, so an MR cannot loosen the checks that judge"],
            ["# The engine version and policy come from the target branch's tip, so an MR cannot loosen the checks that judge"]),
        (
            ["target=\"${SYSTEM_PULLREQUEST_TARGETBRANCH#refs/heads/}\"", "BASE=$(git merge-base HEAD \"origin/$target\")"],
            [
                "# Policy comes from the target branch's tip, not the merge base: a branch cut from an old commit must not bring",
                "# an older engine pin, baselines or analyzer config. The gates work out the merge base themselves.",
                "target=\"${SYSTEM_PULLREQUEST_TARGETBRANCH#refs/heads/}\"",
                "git fetch --quiet origin \"+refs/heads/$target:refs/remotes/origin/$target\"",
                "BASE=$(git rev-parse \"origin/$target\")",
            ]),
        (
            ["# come from the base commit on pull requests, so a PR cannot loosen the checks that judge it. Create a pipeline from this"],
            ["# come from the target branch's tip on pull requests, so a PR cannot loosen the checks that judge it. Create a pipeline from this"]),
    ];

    /// <summary>The migrated text; null when the file must be left alone. The file's own text when it is current already.</summary>
    public static string? Migrate(string text)
    {
        // The CI judge's pipelines install the engine from the base's lock already (GitHub's through its action): nothing to move.
        if (text.Contains("mode: judge", StringComparison.Ordinal) || text.Contains(CiShellScripts.JudgeCommand, StringComparison.Ordinal))
        {
            return text;
        }

        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        List<string> lines = [.. text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')];
        if (!ReplaceInstallStep(lines) || !ReplaceRestore(lines))
        {
            return null;
        }

        foreach ((string[] legacy, string[] current) in BaseBlocks)
        {
            if (Find(lines, legacy) is (int start, string indent))
            {
                lines.RemoveRange(start, legacy.Length);
                lines.InsertRange(start, current.Select(line => indent + line));
            }
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
                if (prefix.Length > 0)
                {
                    GiveTheGitHubStepAToken(lines, start);
                }

                return true;
            }
        }

        return false;
    }

    /// <summary>The GitHub step gets the workflow's token, so the changed-lock check can verify the release's attestation with gh.</summary>
    private static void GiveTheGitHubStepAToken(List<string> lines, int scriptStart)
    {
        int run = scriptStart - 1;
        if (run < 1 || lines[run].Trim() != "run: |" || !lines[run - 1].TrimStart().StartsWith("- name: Install the pinned engine", StringComparison.Ordinal))
        {
            return;
        }

        string indent = Indent(lines[run]);
        lines.InsertRange(run, [$"{indent}env:", $"{indent}  GH_TOKEN: ${{{{ github.token }}}}"]);
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
        // The GitHub workflow's style loop failed under bash -e on a base without style files; it now tolerates none, as the others do.
        int[] styleLoop = [.. Indexes(lines, line => line.Trim() == LegacyGitHubStyleLoop)];
        if (styleLoop.Length == 1)
        {
            lines[styleLoop[0]] = Indent(lines[styleLoop[0]]) + StyleLoop;
        }

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
