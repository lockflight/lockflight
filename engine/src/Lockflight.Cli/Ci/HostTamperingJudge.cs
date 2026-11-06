using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lockflight.Cli.Reasons;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Host tampering (<c>tests.hostTampering</c>): the findings in the head's and every red commit's test assemblies that the base's
/// assemblies did not have and the base's <c>.lockflight/baselines/host-tampering.json</c> does not accept make the check
/// NOT-EARNED, with each one listed; findings the base already had are listed as information, so old code in a fixture does not
/// block. A baseline entry accepts the finding it names (<c>assembly|kind|Type::Method|member</c>) or every finding of its kind in
/// one method (<c>assembly|kind|Type::Method</c>), so a person can accept code that is not written yet.
/// Forging a test result needs deliberate code of this kind, and this makes the plain forms of it visible.
/// </summary>
internal static class HostTamperingJudge
{
    public const string BaselinePath = ".lockflight/baselines/host-tampering.json";

    /// <summary>How a person accepts a legitimate finding, printed with the findings' baseline entries.</summary>
    public const string AcceptAdvice =
        "Accepting a legitimate finding is a person's decision: add its entry to the \"findings\" array of " + BaselinePath
        + " (create it as { \"schema\": \"lockflight.baseline.host-tampering/v1\", \"findings\": [ … ] }) in a pull request of its own to the target branch,"
        + " where the protected file shows as a protected change. The judge reads the baseline from the target branch, never from the pull request it judges:"
        + " once the baseline is merged, push to this pull request or update it from the target branch so the judge runs again."
        + " An entry accepts that method's use of that member and nothing else.";

    private const int MaxListed = 5;
    private const int MinimumIdSeparators = 3;
    private const char Separator = '|';

    public static async Task<TamperingJudgement> DecideAsync(JudgePlan plan, TreeListing head, TreeListing? baseListing, IReadOnlyList<CycleJudgement> cycles, CancellationToken cancellationToken)
    {
        if (head.Outcome == TreeOutcome.NoTestApps)
        {
            return new TamperingJudgement(CheckVerdict.Pass, ReasonCode.None, "no test assembly red/green runs (xUnit, NUnit or MSTest) to scan", [], []);
        }

        if (head.Inventory is null)
        {
            return new TamperingJudgement(CheckVerdict.NotEarned, CheckReasons.NotListed(head.Outcome), "the head's test assemblies could not be built to scan", [], []);
        }

        HashSet<string> atBase = [.. (baseListing?.Tampering ?? []).Select(finding => finding.Id)];
        HashSet<string> accepted = await AcceptedAsync(plan, cancellationToken);
        TamperingFinding[] all = [.. head.Tampering.Concat(cycles.SelectMany(cycle => cycle.Tampering)).DistinctBy(finding => (finding.Id, finding.Member))];
        TamperingFinding[] added = [.. all.Where(finding => !atBase.Contains(finding.Id) && !Accepts(accepted, finding))];
        TamperingFinding[] existing = [.. all.Where(finding => atBase.Contains(finding.Id) || Accepts(accepted, finding))];
        return added.Length > 0
            ? new TamperingJudgement(CheckVerdict.NotEarned, ReasonCode.HostTampering,
                $"new code in the test assemblies that could tamper with the test host: {string.Join("; ", added.Take(MaxListed).Select(Describe))}"
                + $"{(added.Length > MaxListed ? $" and {added.Length - MaxListed} more" : string.Empty)}; a person accepts a legitimate one with the baseline entry listed for it ({BaselinePath} on the target branch)",
                added, existing)
            : new TamperingJudgement(CheckVerdict.Pass, ReasonCode.None,
                existing.Length == 0 ? "no code in the test assemblies that could tamper with the test host" : $"nothing new; {existing.Length} findings the base already had or accepted",
                added, existing);
    }

    /// <summary>
    /// Whether a baseline entry names the finding, or its method (<c>assembly|kind|Type::Method</c>): a person can accept a method
    /// before it exists, but never every future finding of a kind in an assembly, which would switch the check off for it.
    /// </summary>
    public static bool Accepts(IReadOnlySet<string> accepted, TamperingFinding finding) =>
        accepted.Contains(finding.Id)
        || accepted.Any(entry => entry.Count(character => character == '|') == 2 && finding.Id.StartsWith(entry + "|", StringComparison.Ordinal));

    /// <summary>
    /// The finding's entry for the baseline's <c>findings</c> array, as a JSON string ready to paste: it accepts that method's use of
    /// that member and nothing else. Only ASCII is written (anything else and control characters as <c>\uXXXX</c>), so a terminal
    /// with another code page cannot change it in the copy; a generated name such as <c>&lt;Run&gt;</c> reads as it does in the finding.
    /// </summary>
    public static string BaselineEntry(TamperingFinding finding)
    {
        StringBuilder entry = new("\"");
        foreach (char character in finding.Id)
        {
            entry.Append(character switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                < ' ' or > '~' => $"\\u{(int)character:x4}",
                _ => character.ToString(),
            });
        }

        return entry.Append('"').ToString();
    }

    /// <summary>
    /// The finding id a printed baseline entry names, when it is one JSON string naming a whole finding (assembly, kind, method and
    /// member); null otherwise, so an evidence file that was tampered with cannot show a wider entry as a finding's own.
    /// </summary>
    public static string? EntryId(string entry)
    {
        try
        {
            return JsonNode.Parse(entry) is JsonValue value && value.TryGetValue(out string? id) && id.Count(character => character == Separator) >= MinimumIdSeparators
                ? id
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The finding ids a baseline file's text accepts; none when it is missing or not the baseline's JSON.</summary>
    public static HashSet<string> ParseBaseline(string? text)
    {
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

    public static string Describe(TamperingFinding finding) => $"{finding.Kind} in {finding.Location} ({finding.Member})";

    /// <summary>The finding ids the policy commit's baseline accepts, read with git from the policy commit, never the tree.</summary>
    private static async Task<HashSet<string>> AcceptedAsync(JudgePlan plan, CancellationToken cancellationToken) =>
        ParseBaseline(await JudgeGit.ShowFileAsync(plan.Inputs.Root, plan.Policy.Commit, BaselinePath, cancellationToken));
}
