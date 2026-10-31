using Lockflight.Cli.Gates;
using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Lifecycle.Approvals;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// Whether the agent is waiting on a person's first approval of a phase. When red/green is refused because the spec or tasks were
/// never approved (missing, not stale: an agent makes an approval stale with one edit), the refusal records the feature and the
/// tree's inputs hash (<see cref="InputsHash"/>) in <see cref="RelativePath"/> (git-ignored, under the protected <c>.lockflight/</c>).
/// The Stop gate then lets the agent stop with one message, without running the tests it may have written red on purpose, only
/// while: the tree is exactly as it was at the refusal (any edit since puts the gate back), the feature being worked on resolves
/// and is that feature (an unresolved one runs the gate), and that feature's approval is still missing, read again from the
/// approval records. The TaskCompleted hook still holds a task completion meanwhile: nothing passed. A guardrail like the Stop gate
/// itself: CI is the definition of done.
/// </summary>
internal static class PendingApproval
{
    public const string RelativePath = ".lockflight/local/approval-wait";

    /// <summary>Records the wait for a refusal whose phases all lack a first approval; clears any older wait otherwise.</summary>
    public static async Task RememberAsync(string root, Feature feature, IReadOnlyList<ApprovalCheck> notValid, CancellationToken cancellationToken)
    {
        string path = Path.Combine(root, RelativePath);
        try
        {
            if (notValid.Count == 0 || notValid.Any(check => check.State != ApprovalState.Missing)
                || await InputsHash.ComputeAsync(root, cancellationToken) is not string inputsHash)
            {
                File.Delete(path);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, $"{feature.Id}\n{inputsHash}\n", cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Without the record the Stop gate runs as usual: the agent may be held once more, never let through.
        }
    }

    /// <summary>The message for the person while the recorded approval is still pending on the same tree; null otherwise.</summary>
    public static async Task<string?> FindAsync(string root, CancellationToken cancellationToken)
    {
        if (await ReadAsync(root, cancellationToken) is not (string id, string recordedHash)
            || !FeatureLocator.IsPlainId(id)
            || FeatureLocator.Resolve(root, requested: null).Feature is not Feature current
            || current.Id != id
            || await InputsHash.ComputeAsync(root, cancellationToken) != recordedHash)
        {
            return null;
        }

        ApprovalStore approvals = new(current);
        ApprovalCheck[] phases = [approvals.Check(ApprovalPhase.Spec), approvals.Check(ApprovalPhase.Tasks)];
        ApprovalCheck[] pending = [.. phases.Where(check => check.State == ApprovalState.Missing)];
        if (pending.Length == 0 || phases.Any(check => check.State is ApprovalState.Stale or ApprovalState.Invalid))
        {
            return null;
        }

        string needs = string.Join("; ", pending.Select(check => check.Describe(root)));
        return $"lockflight: waiting for a person on feature {id}: {needs}. The stop gate did not run the tests, which may be red "
            + "on purpose until then; it judges the tree again at the next stop after the approval, or after any change.";
    }

    private static async Task<(string Feature, string InputsHash)?> ReadAsync(string root, CancellationToken cancellationToken)
    {
        try
        {
            string path = Path.Combine(root, RelativePath);
            string[] lines = File.Exists(path) ? await File.ReadAllLinesAsync(path, cancellationToken) : [];
            return lines.Length >= 2 && lines[0].Length > 0 && lines[1].Length > 0 ? (lines[0], lines[1]) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
