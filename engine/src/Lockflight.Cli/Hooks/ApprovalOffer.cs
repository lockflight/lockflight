using Lockflight.Cli.Config;
using Lockflight.Cli.Hooks.Events;
using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Lifecycle.Approvals;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// What the person is told when an agent turn ends: the approval that waits for them, once its phase check passes. The Stop hook of
/// a session a person attends records what they can now see (<see cref="SeenArtifacts"/>) and, once per change of the waiting
/// phase's files, returns one line for the person (a <c>systemMessage</c>, which the agent does not see); the status line repeats it
/// while it holds. The files the line was shown for are kept in <see cref="RelativePath"/> (git-ignored, under the protected
/// <c>.lockflight/</c>).
/// </summary>
internal static class ApprovalOffer
{
    public const string FileName = "approval-offered";

    public static string RelativePath => $"{SeenArtifacts.RelativeDirectory}/{FileName}";

    /// <summary>
    /// At the end of an agent turn in an attended, interactive session: records the active feature's artifacts as seen in that
    /// session, and returns the line that offers its waiting approval when it was not offered yet for these files. Null otherwise
    /// (another kind of session, nothing waits, the phase check fails, no feature is active). Never throws: the Stop hook must not
    /// fail on it.
    /// </summary>
    public static string? AtTurnEnd(HookInvocation invocation)
    {
        Manifest manifest = invocation.Manifest;
        if (SessionCommandGate.UnattendedReason(invocation.Context.Environment) is not null)
        {
            return null;
        }

        SeenArtifacts seen = new(manifest.Root);
        try
        {
            if (FeatureLocator.Resolve(manifest.Root, requested: null).Feature is not Feature feature)
            {
                seen.Forget();
                return null;
            }

            // Read once: the seen record and the offer describe the same bytes.
            ApprovalStore store = new(feature);
            IReadOnlyList<ArtifactSnapshot> spec = store.Snapshot(ApprovalPhase.Spec);
            IReadOnlyList<ArtifactSnapshot> tasks = store.Snapshot(ApprovalPhase.Tasks);
            seen.Remember(feature, invocation.Input.Session.Id, [.. spec.Concat(tasks).Select(artifact => (artifact.Name, artifact.Hash))], SeenWhen.TurnEnded);
            if (SeenArtifacts.FileProblem(manifest.Root, FileName) is not null || seen.StateProblem() is not null)
            {
                return null;
            }

            string path = Path.Combine(manifest.Root, RelativePath);
            if (WaitingApproval.Of(feature) is not ApprovalPhase phase || !ApprovalReadiness.Of(manifest, feature, phase).Ready)
            {
                File.Delete(path);
                return null;
            }

            string key = Key(feature, phase, phase == ApprovalPhase.Spec ? spec : tasks);
            if (File.Exists(path) && File.ReadAllText(path) == key)
            {
                return null;
            }

            SeenArtifacts.WriteAtomically(path, key);
            return $"lockflight: {Line(phase)}";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// For the status line: the offer the last turn end made, while it still holds (the same active feature, the same phase waiting,
    /// its files unchanged); null otherwise.
    /// </summary>
    public static string? StatusLine(string root)
    {
        try
        {
            string path = Path.Combine(root, RelativePath);
            if (SeenArtifacts.FileProblem(root, FileName) is not null || !File.Exists(path)
                || FeatureLocator.Resolve(root, requested: null).Feature is not Feature feature
                || WaitingApproval.Of(feature) is not ApprovalPhase phase
                || File.ReadAllText(path) != Key(feature, phase, new ApprovalStore(feature).Snapshot(phase)))
            {
                return null;
            }

            string line = Line(phase);
            return char.ToLowerInvariant(line[0]) + line[1..];
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    private static string Line(ApprovalPhase phase) =>
        $"{(phase == ApprovalPhase.Spec ? "Spec" : "Tasks")} ready for your approval: type /{SessionApproval.CommandName}";

    /// <summary>The feature, the phase and the hashes of its files: an offer is made again only when one of them changes.</summary>
    private static string Key(Feature feature, ApprovalPhase phase, IReadOnlyList<ArtifactSnapshot> artifacts) =>
        $"{feature.Id}\n{ApprovalPhaseNames.ToName(phase)}\n" + string.Concat(artifacts.Select(artifact => $"{artifact.Name}={artifact.Hash}\n"));
}
