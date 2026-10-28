using Lockflight.Cli.Docs;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Doctor;

/// <summary>
/// Doctor's advisory lines about the project's instructions: references that no longer resolve (<c>docs check</c> lists them)
/// and instruction files over Claude Code's size targets. They warn at most: stale documents never stop Lockflight working.
/// </summary>
internal static class DocsChecks
{
    private const string DriftCheck = "docs drift";
    private const string SizeCheck = "instructions size";

    /// <summary>Both lines; none outside a git repository, where there are no tracked documents to read.</summary>
    public static async Task<IReadOnlyList<DoctorCheck>> RunAsync(string root, CancellationToken cancellationToken)
    {
        DocsDriftReport report = await DocsDrift.FindAsync(root, cancellationToken);
        if (!report.Scanned)
        {
            return [];
        }

        DriftFinding[] drift = [.. report.Findings.Where(finding => finding.Kind != DriftKind.Budget)];
        DriftFinding[] size = [.. report.Findings.Where(finding => finding.Kind == DriftKind.Budget)];
        string fix = $"advisory: `{EngineCommand.Linked} docs check` lists each with its reason; fix the documents when it suits";
        return
        [
            drift is [DriftFinding first, ..]
                ? new(DriftCheck, CheckStatus.Warn, $"{drift.Length} reference{Plural(drift.Length)} no longer resolve{(drift.Length == 1 ? "s" : string.Empty)}, first {first.File}:{first.Line} {first.Reference} ({first.Reason})", fix)
                : new(DriftCheck, CheckStatus.Pass, $"{report.Documents} document{Plural(report.Documents)}, every reference resolves", null),
            size.Length > 0
                ? new(SizeCheck, CheckStatus.Warn, string.Join("; ", size.Select(finding => $"{finding.File}: {finding.Reference} ({finding.Reason.Split(". ")[0]})")), fix)
                : new(SizeCheck, CheckStatus.Pass, $"every CLAUDE.md under {InstructionBudget.ClaudeMdLines} lines, every SKILL.md under {InstructionBudget.SkillLines}", null),
        ];
    }

    private static string Plural(int count) => count == 1 ? string.Empty : "s";
}
