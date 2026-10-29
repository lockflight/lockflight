using System.Globalization;
using System.Text.RegularExpressions;
using Lockflight.Cli.Gates.Formatting;
using Lockflight.Cli.Paths;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Gates.Steps;

/// <summary>
/// Whitespace formatting of what the change introduced, in dotnet format's fast folder mode (no workspace load). dotnet format runs
/// on the changed C# files and writes a JSON report; only diagnostics whose span touches a changed line fail the step, so a file's
/// existing formatting debt does not block an unrelated change (<see cref="FormatAttribution"/>). Outside git, or before the first
/// commit, everything is checked and judged whole.
/// </summary>
internal sealed partial class FormatStep : IGateStep
{
    public const string StepName = "format";

    /// <summary>dotnet format's exit code when files would change under --verify-no-changes.</summary>
    private const int ChangesNeeded = 2;

    private const int MaxListed = 5;

    private const int MaxLinesListed = 8;

    /// <summary>
    /// Above this many characters of file names the files go to dotnet format in a response file (one argument per line), well under
    /// Windows' 32,767-character command line.
    /// </summary>
    private const int MaxInlineIncludeCharacters = 8000;

    private const string ResponseFileName = "include.rsp";

    public string Name => StepName;

    public StepScope Scope => StepScope.Root;

    public string CacheKey => StepName;

    public async Task<GateStepResult> RunAsync(GateRunContext run, GateRunFacts facts, CancellationToken cancellationToken) =>
        new(await CheckAsync(run, cancellationToken), facts);

    private static async Task<GateStep> CheckAsync(GateRunContext run, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, IReadOnlySet<int>>? changedLines = await ChangedLines.FindAsync(run.ProjectRoot, run.BaseRef, cancellationToken);
        if (changedLines is null && await Git.HeadAsync(run.ProjectRoot, cancellationToken) is not null)
        {
            return new GateStep(StepName, 0, Verdict.NotEarned, 0, "git could not list the changed lines, so formatting cannot be judged; with --base, is the checkout deep enough (fetch-depth: 0), and does every unit repository have that ref?");
        }

        string[]? files = changedLines is null
            ? null
            : [.. changedLines.Where(entry => entry.Value.Count > 0 && File.Exists(Path.Combine(run.ProjectRoot, entry.Key))).Select(entry => entry.Key).Order(StringComparer.Ordinal)];
        if (files is { Length: 0 })
        {
            return new GateStep(StepName, 0, Verdict.NotApplicable, 0, "no changed C# lines");
        }

        DirectoryInfo reportDirectory = Directory.CreateTempSubdirectory("lockflight-format-");
        try
        {
            List<string> arguments = ["format", "whitespace", run.ProjectRoot, "--folder", "--verify-no-changes", "--report", reportDirectory.FullName];
            arguments.AddRange(await IncludeArgumentsAsync(files, reportDirectory.FullName, cancellationToken));
            ProcessResult formatRun = await run.Processes("dotnet", arguments, run.ProjectRoot, run.TimeLeft(), cancellationToken);
            if (StepHealth.Unhealthy(StepName, formatRun, run) is GateStep unhealthy)
            {
                return unhealthy;
            }

            string checkedFiles = $"{files?.Length.ToString(CultureInfo.InvariantCulture) ?? "all"} files checked";
            GateStep judged = formatRun.ExitCode switch
            {
                0 => new GateStep(StepName, 0, Verdict.Pass, formatRun.DurationMs, checkedFiles),
                ChangesNeeded when changedLines is null => new GateStep(StepName, formatRun.ExitCode, Verdict.Fail, formatRun.DurationMs, DescribeWholeFiles(formatRun.Combined, run.ProjectRoot, "no git history to compare with")),
                ChangesNeeded => await JudgeAsync(run, formatRun, changedLines, Path.Combine(reportDirectory.FullName, FormatReport.FileName), checkedFiles, cancellationToken),
                _ => new GateStep(StepName, formatRun.ExitCode, Verdict.Error, formatRun.DurationMs, $"dotnet format exited {formatRun.ExitCode}"),
            };
            return judged with { Scope = files is null ? "all C# files (format)" : $"{files.Length} changed C# files (format)" };
        }
        finally
        {
            reportDirectory.Delete(recursive: true);
        }
    }

    /// <summary><c>--include</c> with the files; a response file instead when the names would make the command line too long.</summary>
    private static async Task<IReadOnlyList<string>> IncludeArgumentsAsync(string[]? files, string scratchDirectory, CancellationToken cancellationToken)
    {
        if (files is null)
        {
            return [];
        }

        if (files.Sum(file => file.Length + 1) <= MaxInlineIncludeCharacters)
        {
            return ["--include", .. files];
        }

        string responseFile = Path.Combine(scratchDirectory, ResponseFileName);
        await File.WriteAllLinesAsync(responseFile, ["--include", .. files], cancellationToken);
        return [$"@{responseFile}"];
    }

    private static string EndOfLineNote(int count) =>
        $"{count} end-of-line-only differences not attributed to the change: the checked-out line endings differ from .editorconfig's end_of_line (git's core.autocrlf and .gitattributes decide them)";

    private static async Task<GateStep> JudgeAsync(
        GateRunContext run, ProcessResult formatRun, IReadOnlyDictionary<string, IReadOnlySet<int>> changedLines, string reportPath, string checkedFiles, CancellationToken cancellationToken)
    {
        IReadOnlyList<FormatDiagnostic>? diagnostics = File.Exists(reportPath) ? FormatReport.Parse(File.ReadAllText(reportPath), run.ProjectRoot) : null;
        if (diagnostics is null)
        {
            return new GateStep(StepName, formatRun.ExitCode, Verdict.Fail, formatRun.DurationMs, DescribeWholeFiles(formatRun.Combined, run.ProjectRoot, "dotnet format wrote no readable report"));
        }

        if (diagnostics.Count == 0)
        {
            return new GateStep(StepName, formatRun.ExitCode, Verdict.NotEarned, formatRun.DurationMs, "dotnet format reported changes but its report lists none, so the change cannot be judged; run: dotnet format whitespace --folder --verify-no-changes -v diag");
        }

        IReadOnlySet<string> encodingChanged = await EncodingChangedAsync(run, diagnostics, changedLines, cancellationToken);
        FormatJudgement judgement = FormatAttribution.Judge(diagnostics, changedLines, file => ReadText(run.ProjectRoot, file), encodingChanged);
        List<string> notes = [.. Notes(judgement)];

        if (judgement.Attributed.Count == 0)
        {
            return new GateStep(StepName, formatRun.ExitCode, Verdict.Pass, formatRun.DurationMs, string.Join("; ", [checkedFiles, .. notes]));
        }

        string listed = string.Join(", ", judgement.Attributed
            .GroupBy(finding => finding.File)
            .Take(MaxListed)
            .Select(group => $"{group.Key}:{string.Join(',', group.Select(finding => finding.Line).Distinct().Order().Take(MaxLinesListed))} ({string.Join('/', group.Select(finding => finding.DiagnosticId).Distinct())})"));
        if (judgement.WholeFiles.Count > 0)
        {
            notes.Add($"judged whole files (the report's positions could not be matched to the file): {string.Join(", ", judgement.WholeFiles.Take(MaxListed))}");
        }

        string detail = $"formatting changes needed on changed lines: {listed}; fix those lines (dotnet format whitespace --folder --include <file> rewrites whole files)";
        return new GateStep(StepName, formatRun.ExitCode, Verdict.Fail, formatRun.DurationMs, string.Join("; ", [detail, .. notes]));
    }

    /// <summary>What the step saw but did not hold against the change.</summary>
    private static IEnumerable<string> Notes(FormatJudgement judgement)
    {
        if (judgement.EndOfLineOnly > 0)
        {
            yield return EndOfLineNote(judgement.EndOfLineOnly);
        }

        if (judgement.CharsetOnly > 0)
        {
            yield return $"{judgement.CharsetOnly} file encoding differences (byte order mark vs .editorconfig's charset) not attributed to the change: it did not add these files or change their encoding";
        }

        if (judgement.Untouched > 0)
        {
            yield return $"{judgement.Untouched} existing formatting differences on lines this change did not touch are not judged";
        }
    }

    /// <summary>
    /// Of the files with a CHARSET diagnostic, those the change is answerable for: files it adds (every line is a changed line) and
    /// files whose byte order mark it adds or removes. When git cannot show the diff, all of them (fail closed).
    /// </summary>
    private static async Task<IReadOnlySet<string>> EncodingChangedAsync(
        GateRunContext run, IReadOnlyList<FormatDiagnostic> diagnostics, IReadOnlyDictionary<string, IReadOnlySet<int>> changedLines, CancellationToken cancellationToken)
    {
        string[] charsetFiles = [.. diagnostics.Where(diagnostic => diagnostic.DiagnosticId == FormatAttribution.CharsetDiagnostic).Select(diagnostic => diagnostic.File).Distinct(StringComparer.Ordinal)];
        HashSet<string> changed = new(charsetFiles.Where(file => IsAddedWhole(run.ProjectRoot, file, changedLines)), StringComparer.Ordinal);
        string[] pathspecs = [.. charsetFiles.Where(file => !changed.Contains(file)).Select(file => $":(literal){file}")];
        if (pathspecs.Length == 0)
        {
            return changed;
        }

        List<string> ranges = [Git.WorkingTree];
        if (run.BaseRef is not null)
        {
            ranges.Add(Git.SinceMergeBase(run.BaseRef));
        }

        foreach (string range in ranges)
        {
            if (await WorkspaceGit.DiffHunksAsync(run.ProjectRoot, range, pathspecs, run.TimeLeft(), cancellationToken) is not string diff)
            {
                return charsetFiles.ToHashSet(StringComparer.Ordinal);
            }

            changed.UnionWith(UnifiedDiff.FilesOf(diff).Where(file => file.ChangesByteOrderMark).Select(file => file.File));
        }

        return changed;
    }

    /// <summary>A file the change adds: every one of its lines is a changed line (an untracked file counts whole).</summary>
    private static bool IsAddedWhole(string projectRoot, string relativeFile, IReadOnlyDictionary<string, IReadOnlySet<int>> changedLines)
    {
        string path = Path.Combine(projectRoot, relativeFile);
        if (!changedLines.TryGetValue(relativeFile, out IReadOnlySet<int>? lines) || !File.Exists(path))
        {
            return false;
        }

        int lineCount = File.ReadAllLines(path).Length;
        return Enumerable.Range(1, Math.Max(lineCount, 1)).All(lines.Contains);
    }

    private static string? ReadText(string projectRoot, string relativeFile)
    {
        string path = Path.Combine(projectRoot, relativeFile);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static string DescribeWholeFiles(string output, string projectRoot, string why)
    {
        string[] files = [.. FormattingFile().Matches(output).Select(match => PathRules.ToRelative(projectRoot, match.Groups["file"].Value) ?? match.Groups["file"].Value).Distinct(StringComparer.Ordinal)];
        string where = files.Length == 0 ? string.Empty : $" in {string.Join(", ", files.Take(MaxListed))}";
        return $"formatting changes needed{where} (judged whole files: {why}); run: dotnet format whitespace --folder --include <files>";
    }

    [GeneratedRegex(@"^\s*(?<file>[^\r\n(]+\.cs)\(\d+,\d+\): (?:error|warning) [A-Z_]+", RegexOptions.Multiline)]
    private static partial Regex FormattingFile();
}
