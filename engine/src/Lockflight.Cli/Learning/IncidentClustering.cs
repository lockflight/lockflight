using Lockflight.Cli.Gates;
using Lockflight.Cli.Ledger;

namespace Lockflight.Cli.Learning;

/// <summary>
/// Turns ledger records into incident clusters ($0, deterministic). Not incidents: passing gates, accepted reds and greens,
/// approvals, configuration writes, fixed and dismissed review findings, and rejected greens (normal test-first iteration); per-unit Stop times; and the Stop hook's
/// own gate lines, because a red Stop gate is already counted once as its hook block.
/// </summary>
internal static class IncidentClustering
{
    public static IReadOnlyList<IncidentCluster> Cluster(IEnumerable<LedgerRecord> records) =>
        [.. records
            .Where(IsIncident)
            .GroupBy(Fingerprint)
            .Select(group =>
            {
                (IncidentClass incidentClass, RemedyRung rung, string suggestion) = Classify(group.Key);
                LedgerRecord last = group.MaxBy(record => record.At)!;
                return new IncidentCluster(group.Key, group.Count(), group.Min(record => record.At), last.At, last.Summary, incidentClass, rung, suggestion);
            })
            .OrderByDescending(cluster => cluster.Count)
            .ThenBy(cluster => cluster.Fingerprint, StringComparer.Ordinal)];

    public static bool IsIncident(LedgerRecord record) => record.Type switch
    {
        LedgerEventTypeNames.HookBlocked => true,
        LedgerEventTypeNames.GateCompleted => !record.Signature.Contains(LedgerSignatures.GatePassed, StringComparison.Ordinal)
            && !StopTimings.IsTiming(record.Signature) && record.Trigger != GateTriggerNames.StopHook,
        LedgerEventTypeNames.EvidenceRecorded => record.Signature is LedgerSignatures.RedRejected or LedgerSignatures.RedCompileError
            or LedgerSignatures.ReviewChanges or LedgerSignatures.ReviewUnverified,
        LedgerEventTypeNames.ReviewCompleted => record.Signature is LedgerSignatures.ReviewChanges or LedgerSignatures.ReviewUnverified,
        _ => false,
    };

    /// <summary>The signature, refined for rejected reds by the rejection classes (e.g. <c>redgreen:red:rejected:RED_IN_TEST</c>).</summary>
    public static string Fingerprint(LedgerRecord record) =>
        record.Signature == LedgerSignatures.RedRejected && record.Summary.LastIndexOf(": ", StringComparison.Ordinal) is int colon and >= 0
            ? $"{record.Signature}:{record.Summary[(colon + 2)..]}"
            : record.Signature;

    private static (IncidentClass, RemedyRung, string) Classify(string fingerprint) => fingerprint switch
    {
        _ when fingerprint.Contains("protected-path:", StringComparison.Ordinal) =>
            (IncidentClass.RuleIgnored, RemedyRung.Skill, "the guard held; if it recurs, say in the relevant skill which lockflight command changes this file, or why it is protected"),
        _ when fingerprint.Contains("human-only-command", StringComparison.Ordinal) =>
            (IncidentClass.RuleIgnored, RemedyRung.Skill, "the agent tries to record human decisions; make the skill stop and ask the user earlier"),
        _ when fingerprint.Contains("tests-locked:", StringComparison.Ordinal) =>
            (IncidentClass.RuleIgnored, RemedyRung.Skill, "tests edited during red; check whether the reds were weak (review) and reinforce 'fix production code' in the feature skill"),
        _ when fingerprint.Contains(":ERROR", StringComparison.Ordinal) || fingerprint.Contains("timed-out", StringComparison.Ordinal) =>
            (IncidentClass.ToolingGap, RemedyRung.Environment, "an environment or tool problem; run lockflight doctor and fix the setup or the timeout"),
        _ when fingerprint.Contains(":NOT-EARNED", StringComparison.Ordinal) =>
            (IncidentClass.ToolingGap, RemedyRung.Check, "nothing was judged (no tests, no gitleaks, no report); complete the gate configuration"),
        _ when fingerprint.EndsWith(":format", StringComparison.Ordinal) =>
            (IncidentClass.MissingKnowledge, RemedyRung.Analyzer, "formatting failures recur; enable the edit profile (format on edit) or an .editorconfig rule enforced in build"),
        _ when fingerprint.EndsWith(":build", StringComparison.Ordinal) =>
            (IncidentClass.MissingKnowledge, RemedyRung.Analyzer, "new build warnings recur; find the diagnostic IDs and prefer an analyzer setting or code fix over instructions"),
        _ when fingerprint.Contains(":test", StringComparison.Ordinal) =>
            (IncidentClass.MissingKnowledge, RemedyRung.Check, "tests fail at the end of turns; look for the module the failures cluster in and add a check or a path rule there"),
        _ when fingerprint.Contains("coverage.patch", StringComparison.Ordinal) =>
            (IncidentClass.ProcessSkipped, RemedyRung.Skill, "changed code lands untested; reinforce test-first in the feature skill"),
        _ when fingerprint.Contains("security.", StringComparison.Ordinal) =>
            (IncidentClass.MissingKnowledge, RemedyRung.Check, "security findings recur; add a pre-commit check or a path rule for the affected area"),
        _ when fingerprint.StartsWith(LedgerSignatures.RedPrefix, StringComparison.Ordinal) =>
            (IncidentClass.ProcessSkipped, RemedyRung.Skill, "reds are rejected; reinforce stubs-first and assertion-based tests in the feature skill"),
        _ when fingerprint == LedgerSignatures.ReviewChanges =>
            (IncidentClass.MissingKnowledge, RemedyRung.Check, "the review confirmed defects the tests missed; keep the reproductions as regression tests"),
        _ => (IncidentClass.MissingKnowledge, RemedyRung.Check, "inspect the examples and choose the most deterministic rung"),
    };
}
