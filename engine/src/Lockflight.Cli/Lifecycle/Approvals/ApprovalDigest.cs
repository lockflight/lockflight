using System.Text;

namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>
/// What the engine shows a person before they approve in a session: the feature, each artifact with the start of its hash, what the
/// artifacts hold (requirements and stories, or tasks), open clarifications, the current approval, and the line to type with the
/// one-time code. The engine renders it from the files, so it does not depend on the agent's summary.
/// </summary>
internal static class ApprovalDigest
{
    private const int HashPrefixLength = 12;
    private const string ClarificationMarker = "[NEEDS CLARIFICATION";

    /// <param name="typedOptions">Options to repeat on the line to type (<c>--sign</c>, <c>--key file</c>); empty for none.</param>
    public static string Render(Feature feature, ApprovalPhase phase, IReadOnlyList<(string Name, string Hash)> artifacts, ApprovalCheck current, string code, string typedOptions)
    {
        string phaseName = ApprovalPhaseNames.ToName(phase);
        StringBuilder digest = new();
        digest.AppendLine($"lockflight: you are about to approve the {phaseName} of {feature.Id}. Review it (the engine read these files, the agent did not write this):");
        foreach ((string name, string hash) in artifacts)
        {
            digest.AppendLine($"  {name,-10} {(hash.Length > HashPrefixLength ? hash[..HashPrefixLength] : hash)}");
        }

        foreach (string line in phase == ApprovalPhase.Spec ? SpecLines(feature) : TasksLines(feature))
        {
            digest.AppendLine($"  {line}");
        }

        digest.AppendLine($"  Open [NEEDS CLARIFICATION] markers: {Clarifications(feature, artifacts)}");
        digest.AppendLine($"  Current approval: {Describe(current)}");
        digest.AppendLine($"To approve exactly this content, type within {ApprovalCodes.Lifetime.TotalMinutes:0} minutes:");
        digest.AppendLine($"  /lockflight:approve {phaseName} {code}{typedOptions}");
        digest.Append("The code works once, and only while the files stay as shown. The agent does not see this message.");
        return digest.ToString();
    }

    private static IEnumerable<string> SpecLines(Feature feature)
    {
        SpecDocument spec = SpecDocument.Parse(File.ReadAllText(feature.SpecPath));
        yield return spec.Requirements.Count == 0
            ? "Requirements: none found"
            : $"Requirements ({spec.Requirements.Count}): {string.Join(", ", spec.Requirements.Select(requirement => requirement.Id))}";
        yield return spec.Stories.Count == 0
            ? "User stories: none found"
            : $"User stories ({spec.Stories.Count}): {string.Join("; ", spec.Stories.Select(story => $"{story.Number} {story.Title}"))}";
    }

    private static IEnumerable<string> TasksLines(Feature feature)
    {
        IReadOnlyList<TaskLine> tasks = TasksDocument.Parse(File.ReadAllText(feature.TasksPath));
        int tests = tasks.Count(task => task.Role == TaskRole.Test);
        int stubs = tasks.Count(task => task.Role == TaskRole.Stub);
        int implementation = tasks.Count(task => task.Role == TaskRole.Implementation);
        int done = tasks.Count(task => task.IsDone);
        int stories = tasks.Select(task => task.Story).OfType<int>().Distinct().Count();
        string[] covered = [.. tasks.SelectMany(task => task.Requirements).Distinct().Order(StringComparer.Ordinal)];
        yield return $"Tasks ({tasks.Count}): {tests} test, {stubs} stub, {implementation} implementation; {done} already done; {stories} user stories";
        yield return covered.Length == 0 ? "Requirements covered: none named" : $"Requirements covered ({covered.Length}): {string.Join(", ", covered)}";
    }

    private static string Clarifications(Feature feature, IReadOnlyList<(string Name, string Hash)> artifacts)
    {
        List<string> found = [];
        foreach ((string name, _) in artifacts)
        {
            string path = Path.Combine(feature.SpecDirectory, name);
            if (!File.Exists(path))
            {
                continue;
            }

            int[] lines = [.. File.ReadAllLines(path)
                .Select((line, index) => (line, number: index + 1))
                .Where(entry => entry.line.Contains(ClarificationMarker, StringComparison.Ordinal))
                .Select(entry => entry.number)];
            if (lines.Length > 0)
            {
                found.Add($"{name} line{(lines.Length == 1 ? string.Empty : "s")} {string.Join(", ", lines)}");
            }
        }

        return found.Count == 0 ? "none" : string.Join("; ", found);
    }

    private static string Describe(ApprovalCheck current) => current.State switch
    {
        ApprovalState.Valid => $"already approved by {current.ApprovedBy} for this exact content",
        ApprovalState.Stale => $"an earlier approval is stale ({string.Join(", ", current.ChangedArtifacts)} changed since)",
        ApprovalState.Invalid => $"the existing record is not valid here ({current.Problem})",
        _ => "none",
    };
}
