using Lockflight.Cli.Gates.Parsers;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Gates.Steps;

/// <summary>
/// Secret scanning (pr profile) with gitleaks: the commits since the base ref (CI), staged and unstaged changes of tracked files, and
/// untracked files that git does not ignore. gitleaks' <c>dir</c> mode takes one path and does not read <c>.gitignore</c>, so the
/// untracked files (from <c>git ls-files --others --exclude-standard</c>) are copied under their relative paths into a temporary
/// directory only the current user can read, and scanned there in one run; findings and <c>.gitleaksignore</c> fingerprints then
/// carry repository-relative paths. Findings are accepted only through gitleaks' own <c>.gitleaksignore</c>, a protected path; an
/// added inline <c>gitleaks:allow</c> comment — which would silence gitleaks — fails the step (in an untracked file it is also ignored
/// by the scan). Without gitleaks the step cannot vouch for anything, so it is NOT-EARNED rather than PASS.
/// </summary>
internal static class SecretsStep
{
    public const string Name = "security.secrets";

    /// <summary>Exit code requested from gitleaks for findings, distinct from its own error exit (1).</summary>
    private const int LeaksFoundExitCode = 99;

    private const int MaxLeaksListed = 3;

    private const string InlineAllowMarker = "gitleaks:allow";

    private const string ConfigFileName = ".gitleaks.toml";

    /// <summary>
    /// Untracked files larger than this are not searched for an inline allowance; the scan itself still covers them and ignores
    /// allowances there, so skipping the search cannot hide a secret.
    /// </summary>
    private const long MaxAllowanceSearchBytes = 10 * 1024 * 1024;

    public static async Task<GateStep> RunAsync(GateRunContext run, CancellationToken cancellationToken)
    {
        string? gitleaks = Executables.Find("gitleaks", Environment.GetEnvironmentVariable(EnvironmentVariables.Gitleaks));
        if (gitleaks is null)
        {
            return new GateStep(Name, 0, Verdict.NotEarned, 0, $"gitleaks not found; install it on PATH or set {EnvironmentVariables.Gitleaks}");
        }

        if (await WorkspaceGit.UntrackedFilesAsync(run.ProjectRoot, [], run.TimeLeft(), cancellationToken) is not IReadOnlyList<string> listed)
        {
            return new GateStep(Name, 0, Verdict.Error, 0, "git could not list the untracked files; secret scanning needs a git repository");
        }

        string[] untracked = [.. listed.Where(path => IsRegularFile(Path.Combine(run.ProjectRoot, path)))];
        InlineAllowances allowances = await InlineAllowancesAsync(run, untracked, cancellationToken);
        if (allowances.DiffFailedFor is string range)
        {
            return new GateStep(Name, 0, Verdict.Error, 0,
                $"git diff {range} failed, so added '{InlineAllowMarker}' comments cannot be checked; with --base, is the checkout deep enough (fetch-depth: 0), and does every unit repository have that ref?");
        }

        if (allowances.Files.Count > 0)
        {
            return new GateStep(Name, 0, Verdict.Fail, 0,
                $"added lines silence gitleaks with an inline '{InlineAllowMarker}': {string.Join(", ", allowances.Files.Take(MaxLeaksListed))}; accept false positives in .gitleaksignore (human review) instead");
        }

        return await ScanAsync(new GitleaksScan(gitleaks, run), untracked, cancellationToken);
    }

    private static async Task<GateStep> ScanAsync(GitleaksScan scan, string[] untracked, CancellationToken cancellationToken)
    {
        GateRunContext run = scan.Run;
        List<string[]> scopes = [["--pre-commit"], ["--staged"]];
        if (run.BaseRef is not null)
        {
            scopes.Insert(0, [$"--log-opts={run.BaseRef}..HEAD"]);
        }

        DirectoryInfo? staging = null;
        try
        {
            // Every working tree of a workspace is scanned as its own repository; findings in a child carry its unit path.
            foreach (RepoRoot repo in WorkspaceRepos.Of(run.ProjectRoot))
            {
                foreach (string[] scope in scopes)
                {
                    if (await scan.RunAsync(["git", .. scope, .. scan.ReportArguments, repo.Directory], repo.Directory, scope[0], repo.Prefix, cancellationToken) is GateStep problem)
                    {
                        return problem;
                    }
                }
            }

            if (untracked.Length > 0)
            {
                staging = Stage(run.ProjectRoot, untracked);
                string[] dirScan = ["dir", .. scan.ReportArguments, "--ignore-gitleaks-allow", "--gitleaks-ignore-path", run.ProjectRoot, .. ConfigArguments(run.ProjectRoot), "."];
                if (await scan.RunAsync(dirScan, staging.FullName, "untracked files", string.Empty, cancellationToken) is GateStep problem)
                {
                    return problem;
                }
            }
        }
        finally
        {
            scan.DeleteReport();
            staging?.Delete(recursive: true);
        }

        return Judge(run, scan, untracked.Length);
    }

    private static GateStep Judge(GateRunContext run, GitleaksScan scan, int untrackedCount)
    {
        SecretLeak[] distinct = [.. scan.Leaks.Distinct()];
        string scanned = $"uncommitted changes and {untrackedCount} untracked files scanned (pass --base to scan the branch's commits)";
        return distinct.Length == 0
            ? new GateStep(Name, 0, Verdict.Pass, scan.DurationMs, run.BaseRef is null ? scanned : string.Empty)
            : new GateStep(Name, LeaksFoundExitCode, Verdict.Fail, scan.DurationMs,
                $"{distinct.Length} possible secrets: {string.Join("; ", distinct.Take(MaxLeaksListed).Select(leak => leak.Describe()))}; rotate them, or accept a false positive in .gitleaksignore (human review)");
    }

    /// <summary>
    /// Files whose added lines (uncommitted, plus since the base) contain <c>gitleaks:allow</c>; for an untracked file every line is
    /// added. A diff git cannot produce is reported, never read as "no allowances".
    /// </summary>
    private static async Task<InlineAllowances> InlineAllowancesAsync(GateRunContext run, string[] untracked, CancellationToken cancellationToken)
    {
        if (await Git.UncommittedBaseAsync(run.ProjectRoot, cancellationToken) is not string uncommittedBase)
        {
            return new InlineAllowances([], Git.WorkingTree);
        }

        List<string> ranges = [uncommittedBase];
        if (run.BaseRef is not null)
        {
            ranges.Add(Git.SinceMergeBase(run.BaseRef));
        }

        HashSet<string> files = new(StringComparer.Ordinal);
        foreach (string range in ranges)
        {
            if (await WorkspaceGit.DiffHunksAsync(run.ProjectRoot, range, [], run.TimeLeft(), cancellationToken) is not string diff)
            {
                return new InlineAllowances([], range);
            }

            files.UnionWith(UnifiedDiff.FilesOf(diff)
                .Where(file => file.Added.Any(line => line.Text.Contains(InlineAllowMarker, StringComparison.OrdinalIgnoreCase)))
                .Select(file => file.File));
        }

        foreach (string path in untracked)
        {
            string fullPath = Path.Combine(run.ProjectRoot, path);
            if (new FileInfo(fullPath).Length <= MaxAllowanceSearchBytes
                && (await File.ReadAllTextAsync(fullPath, cancellationToken)).Contains(InlineAllowMarker, StringComparison.OrdinalIgnoreCase))
            {
                files.Add(path);
            }
        }

        return new InlineAllowances([.. files.Order(StringComparer.Ordinal)], DiffFailedFor: null);
    }

    /// <summary>
    /// Copies the untracked files under their repository-relative paths into a new temporary directory, so the scan reports and
    /// fingerprints those paths. The directory is created for the current user only (mode 0700 on Unix): the copies may hold secrets.
    /// </summary>
    private static DirectoryInfo Stage(string root, string[] untracked)
    {
        DirectoryInfo staging = Directory.CreateTempSubdirectory("lockflight-untracked-");
        if (!OperatingSystem.IsWindows())
        {
            staging.UnixFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        }

        foreach (string path in untracked)
        {
            string target = Path.Combine(staging.FullName, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(root, path), target);
        }

        return staging;
    }

    /// <summary>gitleaks reads <c>.gitleaks.toml</c> from the scanned path; the staging copy is elsewhere, so name the project's config.</summary>
    private static string[] ConfigArguments(string root)
    {
        string config = Path.Combine(root, ConfigFileName);
        return File.Exists(config) ? ["--config", config] : [];
    }

    /// <summary>A file gitleaks would scan: it exists and is not a symbolic link (gitleaks does not follow links).</summary>
    private static bool IsRegularFile(string path) => File.Exists(path) && new FileInfo(path).LinkTarget is null;

    /// <summary>Files that add an inline allowance, or the range whose diff git could not produce.</summary>
    private sealed record InlineAllowances(IReadOnlyList<string> Files, string? DiffFailedFor);

    /// <summary>One gitleaks report file reused by every scan of the step, with the findings and time collected so far.</summary>
    private sealed class GitleaksScan(string gitleaks, GateRunContext run)
    {
        private readonly string reportPath = Path.Combine(Path.GetTempPath(), $"lockflight-gitleaks-{Guid.CreateVersion7()}.json");

        public GateRunContext Run => run;

        public List<SecretLeak> Leaks { get; } = [];

        public long DurationMs { get; private set; }

        public string[] ReportArguments =>
            ["--redact", "--no-banner", "--report-format", "json", "--report-path", reportPath, "--exit-code", $"{LeaksFoundExitCode}"];

        /// <summary>
        /// Runs one scan and collects its findings, their paths under <paramref name="pathPrefix"/> (a child repository's unit path);
        /// a step that says why when gitleaks itself failed.
        /// </summary>
        public async Task<GateStep?> RunAsync(string[] arguments, string workingDirectory, string label, string pathPrefix, CancellationToken cancellationToken)
        {
            ProcessResult scan = await ProcessRunner.RunAsync(gitleaks, arguments, workingDirectory, run.TimeLeft(), cancellationToken);
            DurationMs += scan.DurationMs;
            if (StepHealth.Unhealthy(Name, scan, run) is GateStep unhealthy)
            {
                return unhealthy;
            }

            if (scan.ExitCode is not 0 and not LeaksFoundExitCode)
            {
                return new GateStep(Name, scan.ExitCode, Verdict.Error, DurationMs, $"gitleaks ({label}) exited {scan.ExitCode}: {LastLine(scan.Combined)}");
            }

            Leaks.AddRange(SecretLeaks.Parse(File.Exists(reportPath) ? await File.ReadAllTextAsync(reportPath, cancellationToken) : string.Empty)
                .Select(leak => leak with { File = pathPrefix + leak.File }));
            return null;
        }

        public void DeleteReport() => File.Delete(reportPath);

        private static string LastLine(string output) =>
            output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? string.Empty;
    }
}
