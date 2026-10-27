using System.Text;

namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>
/// What the engine shows a person before they approve in a session: the feature, each artifact with the start of its hash, what the
/// artifacts hold (requirements and stories, or tasks), open clarifications, the current approval, and the line to type with the
/// one-time code. The engine renders it from the same bytes it hashed (<see cref="ArtifactSnapshot"/>), so it does not depend on the
/// agent's summary, and a file rewritten while it renders cannot make the digest describe other content than the code binds.
/// </summary>
internal static class ApprovalDigest
{
    private const int HashPrefixLength = 12;
    private const string ClarificationMarker = "[NEEDS CLARIFICATION";

    /// <param name="typedOptions">Options to repeat on the line to type (<c>--sign</c>, <c>--key file</c>); empty for none.</param>
    /// <param name="artifacts">The phase's artifacts as read once (<see cref="ApprovalStore.Snapshot"/>), its main one first.</param>
    public static string Render(Feature feature, ApprovalPhase phase, IReadOnlyList<ArtifactSnapshot> artifacts, ApprovalCheck current, string code, string typedOptions)
    {
        string phaseName = ApprovalPhaseNames.ToName(phase);
        StringBuilder digest = new();
        digest.AppendLine($"lockflight: you are about to approve the {phaseName} of {feature.Id}. Review it (the engine read these files, the agent did not write this):");
        foreach (ArtifactSnapshot artifact in artifacts)
        {
            string hash = artifact.Hash;
            digest.AppendLine($"  {artifact.Name,-10} {(hash.Length > HashPrefixLength ? hash[..HashPrefixLength] : hash)}");
        }

        string main = artifacts[0].Text ?? string.Empty;
        foreach (string line in phase == ApprovalPhase.Spec ? SpecLines(main) : TasksLines(main))
        {
            digest.AppendLine($"  {line}");
        }

        digest.AppendLine($"  Open [NEEDS CLARIFICATION] markers: {Clarifications(artifacts)}");
        digest.AppendLine($"  Current approval: {Describe(current)}");
        digest.AppendLine($"To approve exactly this content, type within {ApprovalCodes.Lifetime.TotalMinutes:0} minutes:");
        digest.AppendLine($"  /lockflight:approve {phaseName} {code}{typedOptions}");
        digest.Append("The code works once, and only while the files stay as shown. The agent does not see this message.");
        return digest.ToString();
    }

    private static IEnumerable<string> SpecLines(string text)
    {
        SpecDocument spec = SpecDocument.Parse(text);
        yield return spec.Requirements.Count == 0
            ? "Requirements: none found"
            : $"Requirements ({spec.Requirements.Count}): {string.Join(", ", spec.Requirements.Select(requirement => requirement.Id))}";
        yield return spec.Stories.Count == 0
            ? "User stories: none found"
            : $"User stories ({spec.Stories.Count}): {string.Join("; ", spec.Stories.Select(story => $"{story.Number} {story.Title}"))}";
    }

    private static IEnumerable<string> TasksLines(string text)
    {
        IReadOnlyList<TaskLine> tasks = TasksDocument.Parse(text);
        int tests = tasks.Count(task => task.Role == TaskRole.Test);
        int stubs = tasks.Count(task => task.Role == TaskRole.Stub);
        int implementation = tasks.Count(task => task.Role == TaskRole.Implementation);
        int done = tasks.Count(task => task.IsDone);
        int stories = tasks.Select(task => task.Story).OfType<int>().Distinct().Count();
        string[] covered = [.. tasks.SelectMany(task => task.Requirements).Distinct().Order(StringComparer.Ordinal)];
        yield return $"Tasks ({tasks.Count}): {tests} test, {stubs} stub, {implementation} implementation; {done} already done; {stories} user stories";
        yield return covered.Length == 0 ? "Requirements covered: none named" : $"Requirements covered ({covered.Length}): {string.Join(", ", covered)}";
    }

    private static string Clarifications(IReadOnlyList<ArtifactSnapshot> artifacts)
    {
        List<string> found = [];
        foreach (ArtifactSnapshot artifact in artifacts)
        {
            if (artifact.Text is not string text)
            {
                continue;
            }

            string name = artifact.Name;
            int[] lines = [.. text.ReplaceLineEndings("\n").Split('\n')
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
