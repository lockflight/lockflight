using System.Diagnostics;
using Lockflight.Cli.Config;
using Lockflight.Cli.Docs;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Gates.Steps;

/// <summary>
/// References in the agent's instruction files (<see cref="InstructionFiles"/>) that this change broke (pr profile): links and
/// anchors, code-span paths with evidence of drift, and engine and plugin command lines, the checks of <c>docs check</c>. The
/// working tree is read, then the same check runs on the comparison commit — the merge base with the base ref, or without one the
/// last commit — read through git without a checkout. Only a broken reference the comparison commit did not have fails; one that
/// was already broken is listed and left to a person. A reference is the same when its file, text and kind are, wherever its line
/// moved. <c>gates.docs.references</c> decides whether the step can fail, only reports, or is off.
/// </summary>
internal sealed class DocsReferencesStep : IGateStep
{
    public const string StepName = "docs.references";

    public const string Setting = "gates.docs.references";

    private const int MaxListed = 10;

    private const int MaxOlderListed = 5;

    public string Name => StepName;

    public StepScope Scope => StepScope.Root;

    public string CacheKey => StepName;

    public GateMode ModeUnder(GateSettings settings) => settings.DocsReferences;

    public async Task<GateStepResult> RunAsync(GateRunContext run, GateRunFacts facts, CancellationToken cancellationToken)
    {
        GateMode mode = run.Settings.DocsReferences;
        GateStep step = mode == GateMode.Off ? StepModes.Off(StepName, Setting) : StepModes.Apply(await CheckAsync(run, cancellationToken), mode, Setting);
        return new GateStepResult(step, facts);
    }

    private static async Task<GateStep> CheckAsync(GateRunContext run, CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        if (await RepositoryFiles.LoadAsync(run.ProjectRoot, cancellationToken) is not RepositoryFiles head)
        {
            return new GateStep(StepName, 0, Verdict.Error, stopwatch.ElapsedMilliseconds, "not a git repository; the references check reads the tracked files");
        }

        string[] documents = [.. head.Present.Where(InstructionFiles.IsInstruction).Order(StringComparer.Ordinal)];
        if (documents.Length == 0)
        {
            return new GateStep(StepName, 0, Verdict.NotApplicable, stopwatch.ElapsedMilliseconds, "no instruction files (CLAUDE.md, AGENTS.md, .claude/)");
        }

        string? comparison = run.BaseRef is null
            ? await Git.HeadAsync(run.ProjectRoot, cancellationToken)
            : await Git.MergeBaseAsync(run.ProjectRoot, run.BaseRef, cancellationToken);
        if (comparison is null)
        {
            return run.BaseRef is null
                ? new GateStep(StepName, 0, Verdict.NotApplicable, stopwatch.ElapsedMilliseconds, "no commit to compare with yet")
                : new GateStep(StepName, 0, Verdict.Error, stopwatch.ElapsedMilliseconds, $"no merge base with '{run.BaseRef}'; is the checkout deep enough (fetch-depth: 0)?");
        }

        if (await RepositoryFiles.AtCommitAsync(run.ProjectRoot, comparison, run.TimeLeft(), cancellationToken) is not RepositoryFiles before)
        {
            return new GateStep(StepName, 0, Verdict.Error, stopwatch.ElapsedMilliseconds, $"git could not read the files of {comparison[..Math.Min(12, comparison.Length)]}");
        }

        IReadOnlyList<DriftFinding> now = await DocsDrift.ReferencesAsync(head, documents, cancellationToken);
        IReadOnlyList<DriftFinding> then = await DocsDrift.ReferencesAsync(before, before.Tracked.Where(InstructionFiles.IsInstruction).Order(StringComparer.Ordinal), cancellationToken);
        HashSet<(string, string, DriftKind)> brokenBefore = [.. then.Select(Key)];
        DriftFinding[] broken = [.. now.Where(finding => !brokenBefore.Contains(Key(finding)))];
        DriftFinding[] older = [.. now.Where(finding => brokenBefore.Contains(Key(finding)))];

        string olderNote = older.Length == 0
            ? string.Empty
            : $"{older.Length} references broken before this change, not judged: {List(older, MaxOlderListed)}";
        if (broken.Length == 0)
        {
            string[] parts = [$"{documents.Length} instruction files checked", olderNote];
            return new GateStep(StepName, 0, Verdict.Pass, stopwatch.ElapsedMilliseconds, string.Join("; ", parts.Where(part => part.Length > 0)));
        }

        string[] failure = [$"{broken.Length} references this change broke: {List(broken, MaxListed)}; fix the reference, or what it names", olderNote];
        return new GateStep(StepName, 0, Verdict.Fail, stopwatch.ElapsedMilliseconds, string.Join("; ", failure.Where(part => part.Length > 0)));
    }

    private static (string, string, DriftKind) Key(DriftFinding finding) => (finding.File, finding.Reference, finding.Kind);

    private static string List(DriftFinding[] findings, int max)
    {
        string listed = string.Join("; ", findings.Take(max).Select(finding => $"{finding.File}:{finding.Line} `{finding.Reference}` ({finding.Reason})"));
        return findings.Length > max ? $"{listed}; and {findings.Length - max} more" : listed;
    }
}
