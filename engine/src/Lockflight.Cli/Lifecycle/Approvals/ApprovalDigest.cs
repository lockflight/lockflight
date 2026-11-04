using System.Text;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>
/// What the engine shows a person about an approval in a session, before (when the content changed since they could last see it) or
/// once it is recorded: each artifact with the start of its hash, what the artifacts hold (requirements and stories, or tasks), the
/// requirements marked as verified outside .NET with their text and files (trace leaves them out, so a person sees each one), open
/// clarifications and, before, the current approval. The engine renders it from the same bytes it hashed
/// (<see cref="ArtifactSnapshot"/>), so it does not depend on the agent's summary, and a file rewritten while it renders cannot make
/// it describe other content than the approval binds.
/// </summary>
internal static class ApprovalDigest
{
    private const int HashPrefixLength = 12;
    private const string ClarificationMarker = "[NEEDS CLARIFICATION";

    /// <param name="artifacts">The phase's artifacts as read once (<see cref="ApprovalStore.Snapshot"/>), its main one first.</param>
    /// <param name="current">The approval recorded now, for a line saying so; null leaves the line out (the approval was just recorded).</param>
    /// <returns>Indented lines, without a final line break.</returns>
    public static string Render(ApprovalPhase phase, IReadOnlyList<ArtifactSnapshot> artifacts, ApprovalCheck? current)
    {
        StringBuilder digest = new();
        foreach (ArtifactSnapshot artifact in artifacts)
        {
            string hash = artifact.Hash;
            digest.AppendLine($"  {artifact.Name,-10} {(artifact.Content is null ? hash : $"hash {hash[..Math.Min(HashPrefixLength, hash.Length)]}")}");
        }

        string main = artifacts[0].Text ?? string.Empty;
        foreach (string line in phase == ApprovalPhase.Spec ? SpecLines(main) : TasksLines(main))
        {
            digest.AppendLine($"  {line}");
        }

        digest.Append($"  Open [NEEDS CLARIFICATION] markers: {Clarifications(artifacts)}");
        if (current is not null)
        {
            digest.Append($"\n  Current approval: {Describe(current)}");
        }

        return digest.ToString();
    }

    private static IEnumerable<string> SpecLines(string text)
    {
        SpecDocument spec = SpecDocument.Parse(text);
        yield return spec.Requirements.Count == 0
            ? "Requirements: none found"
            : $"Requirements ({spec.Requirements.Count}): {string.Join(", ", spec.Requirements.Select(requirement => requirement.Id))}";
        Requirement[] outside = [.. spec.Requirements.Where(requirement => requirement.VerifiedOutside.Count > 0)];
        if (outside.Length > 0)
        {
            yield return $"Verified outside .NET, not traced to .NET tests ({outside.Length}):";
            foreach (Requirement requirement in outside)
            {
                yield return TerminalText.Clean($"  {requirement.Id} {requirement.Text} — {string.Join(", ", requirement.VerifiedOutside)}");
            }
        }

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
