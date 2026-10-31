using System.Text.Json.Nodes;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// What the report job works out from git itself before it trusts the judge's evidence: the evidence was written in the job that
/// ran the pull request's code, but which checks are required needs no build. With the target branch's tip checked out and the
/// head fetched, the report re-plans (step P0: policy, class, red commits, levels) and refuses evidence that names another policy
/// commit, another engine version or event, another class, other red commits, or a check at a lower level than the plan's floor.
/// The floor allows what the judge may lower after planning: trace when its requirement is "when applicable" (off without a
/// spec), approvals with <c>approvals.check: report</c> (advisory). Raising a level is allowed.
/// </summary>
internal static class EvidenceCrossCheck
{
    /// <summary>Why the evidence cannot be the judge's for this head and policy; null when it agrees with the plan.</summary>
    public static async Task<string?> MismatchAsync(JsonObject evidence, JudgeInputs inputs, EnvironmentReader environment, CancellationToken cancellationToken)
    {
        string? policy = Text(evidence["policy"]?["from"]);
        if (policy != inputs.Base)
        {
            return $"it was judged with the policy of {Short(policy)}, not the target branch's {Short(inputs.Base)}";
        }

        if (Text(evidence["engine"]?["version"]) is string version && version != ToolPin.RunningVersion)
        {
            return $"it was written by engine {TerminalText.Clean(version)}, and the target branch pins {ToolPin.RunningVersion}";
        }

        if (Text(evidence["event"]) != JudgeEventNames.ToName(inputs.Event))
        {
            return $"its event is {TerminalText.Clean(Text(evidence["event"]) ?? "none")}, not {JudgeEventNames.ToName(inputs.Event)}";
        }

        JudgePlanning planning = await JudgePlanner.PlanAsync(inputs, environment, cancellationToken);
        if (planning.Plan is not JudgePlan plan)
        {
            return $"the report could not plan the run from git to compare with ({planning.Error}); fetch the head and the target branch";
        }

        string planned = ChangeClassNames.ToName(plan.Class.Classification.Class);
        if (Text(evidence["changeClass"]?["class"]) != planned)
        {
            return $"its change class is {TerminalText.Clean(Text(evidence["changeClass"]?["class"]) ?? "none")}, and git gives {planned}";
        }

        string[] reds = [.. plan.Reds.All.Select(red => red.Commit)];
        string[] listed = [.. (evidence["redCommits"]?.AsArray() ?? []).Select(node => Text(node?["commit"]) ?? string.Empty)];
        if (!reds.SequenceEqual(listed, StringComparer.Ordinal))
        {
            return $"it lists {listed.Length} red commits, and git gives {reds.Length}";
        }

        foreach (JsonNode? node in evidence["checks"]?.AsArray() ?? [])
        {
            if (CheckIdNames.Parse(Text(node?["id"]) ?? string.Empty) is CheckId id
                && CheckLevelNames.Parse(Text(node?["requirement"]) ?? string.Empty) is CheckLevel level
                && Rank(level) < Rank(Floor(plan, id)))
            {
                return $"it has {CheckIdNames.ToName(id)} {CheckLevelNames.ToName(level)}, and the plan makes it {CheckLevelNames.ToName(Floor(plan, id))}";
            }
        }

        return null;
    }

    /// <summary>The lowest level the judge may report for a check after it planned <paramref name="plan"/>.</summary>
    public static CheckLevel Floor(JudgePlan plan, CheckId id)
    {
        CheckLevel level = plan.Levels[id];
        return id switch
        {
            CheckId.Trace when plan.Requirements.LevelOf(RequirementKind.SpecAndTrace) == RequirementLevel.WhenApplicable => CheckLevel.Off,
            CheckId.Approvals when level == CheckLevel.Required && (plan.Policy.Manifest?.Approvals ?? ApprovalSettings.ForVersion1).Check == ApprovalCheckMode.Report => CheckLevel.Advisory,
            _ => level,
        };
    }

    private static int Rank(CheckLevel level) => level switch
    {
        CheckLevel.Required => 2,
        CheckLevel.Advisory => 1,
        _ => 0,
    };

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static string Short(string? commit) => commit is null ? "none" : CommitIds.Short(TerminalText.Clean(commit));
}
