using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Host tampering (<c>tests.hostTampering</c>): the findings in the head's and every red commit's test assemblies that the base's
/// assemblies did not have and the base's <c>.lockflight/baselines/host-tampering.json</c> does not accept make the check
/// NOT-EARNED, with each one listed; findings the base already had are listed as information, so old code in a fixture does not
/// block. Forging a test result needs deliberate code of this kind, and this makes the plain forms of it visible.
/// </summary>
internal static class HostTamperingJudge
{
    public const string BaselinePath = ".lockflight/baselines/host-tampering.json";

    private const int MaxListed = 5;

    public static async Task<TamperingJudgement> DecideAsync(JudgePlan plan, TreeListing head, TreeListing? baseListing, IReadOnlyList<CycleJudgement> cycles, CancellationToken cancellationToken)
    {
        if (head.Outcome == TreeOutcome.NoTestApps)
        {
            return new TamperingJudgement(CheckVerdict.Pass, "no test assembly red/green runs (xUnit or NUnit) to scan", [], []);
        }

        if (head.Inventory is null)
        {
            return new TamperingJudgement(CheckVerdict.NotEarned, "the head's test assemblies could not be built to scan", [], []);
        }

        HashSet<string> atBase = [.. (baseListing?.Tampering ?? []).Select(finding => finding.Id)];
        HashSet<string> accepted = await AcceptedAsync(plan, cancellationToken);
        TamperingFinding[] all = [.. head.Tampering.Concat(cycles.SelectMany(cycle => cycle.Tampering)).DistinctBy(finding => (finding.Id, finding.Member))];
        TamperingFinding[] added = [.. all.Where(finding => !atBase.Contains(finding.Id) && !accepted.Contains(finding.Id))];
        TamperingFinding[] existing = [.. all.Where(finding => atBase.Contains(finding.Id) || accepted.Contains(finding.Id))];
        return added.Length > 0
            ? new TamperingJudgement(CheckVerdict.NotEarned,
                $"new code in the test assemblies that could tamper with the test host: {string.Join("; ", added.Take(MaxListed).Select(Describe))}"
                + $"{(added.Length > MaxListed ? $" and {added.Length - MaxListed} more" : string.Empty)}; a person accepts a legitimate one in {BaselinePath} on the target branch",
                added, existing)
            : new TamperingJudgement(CheckVerdict.Pass,
                existing.Length == 0 ? "no code in the test assemblies that could tamper with the test host" : $"nothing new; {existing.Length} findings the base already had or accepted",
                added, existing);
    }

    public static string Describe(TamperingFinding finding) => $"{finding.Kind} in {finding.Location} ({finding.Member})";

    /// <summary>The finding ids the policy commit's baseline accepts, read with git from the policy commit, never the tree.</summary>
    private static async Task<HashSet<string>> AcceptedAsync(JudgePlan plan, CancellationToken cancellationToken)
    {
        string? text = await JudgeGit.ShowFileAsync(plan.Inputs.Root, plan.Policy.Commit, BaselinePath, cancellationToken);
        try
        {
            return text is not null && JsonNode.Parse(text)?["findings"] is JsonArray findings
                ? [.. findings.Select(entry => entry?.GetValue<string>() ?? string.Empty).Where(entry => entry.Length > 0)]
                : [];
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return [];
        }
    }
}
