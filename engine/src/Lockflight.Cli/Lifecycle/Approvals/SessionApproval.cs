using Lockflight.Cli.Config;
using Lockflight.Cli.Hooks;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;

namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>
/// <c>/lockflight:approve spec|tasks [--feature id] [--sign [--key file]]</c> typed by the person in Claude Code (the two steps and
/// their checks: <see cref="SessionCommandGate"/>). Without a code the engine shows the person what would be approved with a one-time
/// code bound to the artifacts' hashes; with that code it records the approval exactly as <c>lockflight approve</c> does. Both steps
/// first run the matching phase check and refuse while it fails (<see cref="ApprovalReadiness"/>). Signed approvals verified in CI
/// are the boundary.
/// </summary>
internal sealed class SessionApproval(CliContext context, Manifest manifest)
{
    public const string CommandName = "lockflight:approve";

    private static readonly string[] ValueOptions = ["--feature", "--key"];
    private static readonly string[] Flags = ["--sign"];

    public static SessionCommandSpec Spec { get; } = new(CommandName, "approve", "nothing was approved", "approves", "session-approval");

    private SessionCommandGate Gate => new(context, manifest, Spec);

    public async Task<SessionCommandResult> DecideAsync(HookInput input, CancellationToken cancellationToken)
    {
        string[] words = SessionCommandGate.Words(input);
        if (Gate.NotTypedByAPerson(input, "records an approval") is SessionRefusal notTyped)
        {
            return Refuse(notTyped, words);
        }

        if (words.Length == 0 && PendingStep() is SessionCommandResult pending)
        {
            return pending;
        }

        if (words.Length == 0 || !ApprovalPhaseNames.TryParse(words[0], out ApprovalPhase phase))
        {
            return Refuse(new SessionRefusal("usage", $"type /{CommandName} spec or /{CommandName} tasks (optionally with --feature <id> and --sign)"), words);
        }

        if (SessionCommandGate.SplitCode(words[1..], ValueOptions) is not { } split
            || CommandOptions.Parse(split.OptionWords, ValueOptions, Flags) is not { Options: CommandOptions options })
        {
            return Refuse(new SessionRefusal("usage", $"/{CommandName} takes {words[0]}, then optionally --feature <id>, --sign, --key <file> and the code it showed you"), words);
        }

        if (options.Value("--feature") is string requested && !FeatureLocator.IsPlainId(requested))
        {
            return Refuse(new SessionRefusal("usage", $"--feature takes a feature ID, the name of a directory under {FeatureLocator.SpecsDirectory}/, not a path"), words);
        }

        if (Gate.UnsafeState(ApprovalPhaseNames.ToName(phase)) is SessionRefusal unsafeState)
        {
            return Refuse(unsafeState, words);
        }

        return split.Code is string code
            ? await RecordAsync(phase, code, options, words, cancellationToken)
            : Show(phase, options, words);
    }

    /// <summary>The line to type when a code waits for a phase and the person typed the command alone; null when none waits.</summary>
    private SessionCommandResult? PendingStep()
    {
        foreach (ApprovalPhase waitingPhase in new[] { ApprovalPhase.Spec, ApprovalPhase.Tasks })
        {
            string name = ApprovalPhaseNames.ToName(waitingPhase);
            if (Gate.Codes.Waiting(name, FeatureLocator.IsPlainId) is SessionCodeWaiting waiting)
            {
                return Gate.Pending(waiting, $"the {name} of {waiting.Subject}", $"/{CommandName} {name}", $"To see it again with a new code, type /{CommandName} {name}");
            }
        }

        return null;
    }

    private SessionCommandResult Show(ApprovalPhase phase, CommandOptions options, string[] words)
    {
        FeatureLookup lookup = FeatureLocator.Resolve(manifest.Root, options.Value("--feature"));
        if (lookup.Feature is not Feature feature)
        {
            return Refuse(new SessionRefusal("no-feature", lookup.Error!), words);
        }

        // Each artifact is read once: the code binds the hashes of exactly the bytes the digest describes.
        ApprovalStore store = new(feature);
        IReadOnlyList<ArtifactSnapshot> snapshot = store.Snapshot(phase);
        if (snapshot[0].Content is null)
        {
            string primary = Path.Combine(feature.SpecDirectory, snapshot[0].Name);
            return Refuse(new SessionRefusal("nothing-to-approve", $"{Path.GetRelativePath(manifest.Root, primary)} does not exist; nothing to approve"), words);
        }

        if (ApprovalReadiness.Of(manifest, feature, phase) is { Ready: false } readiness)
        {
            return Refuse(new SessionRefusal("phase-check", readiness.Explanation), words);
        }

        (string Name, string Hash)[] artifacts = [.. snapshot.Select(artifact => (artifact.Name, artifact.Hash))];
        string code = Gate.Codes.Issue(ApprovalPhaseNames.ToName(phase), feature.Id, artifacts);
        string typedOptions = (options.Flag("--sign") ? " --sign" : string.Empty)
            + (options.Value("--key") is string key ? $" --key {key}" : string.Empty);
        return SessionCommandResult.Shown(ApprovalDigest.Render(feature, phase, snapshot, store.Check(phase, artifacts), code, typedOptions));
    }

    private async Task<SessionCommandResult> RecordAsync(ApprovalPhase phase, string code, CommandOptions options, string[] words, CancellationToken cancellationToken)
    {
        string phaseName = ApprovalPhaseNames.ToName(phase);
        string again = $"type /{CommandName} {phaseName} to see it again with a new code";
        SessionCodes codes = Gate.Codes;
        SessionCodeCheck check = codes.Check(phaseName, code, FeatureLocator.IsPlainId);
        if (SessionCommandGate.CodeProblem(check, "approval code", $"the {phaseName}", again) is SessionRefusal codeProblem)
        {
            return Refuse(codeProblem, words);
        }

        if (options.Value("--feature") is string requested && requested != check.Subject)
        {
            return Refuse(new SessionRefusal("other-feature", $"the code was shown for feature {check.Subject}, not {requested}; {again}"), words);
        }

        if (FeatureLocator.Resolve(manifest.Root, check.Subject).Feature is not Feature feature)
        {
            codes.Discard(phaseName);
            return Refuse(new SessionRefusal("no-feature", $"feature {check.Subject} no longer exists under {FeatureLocator.SpecsDirectory}/"), words);
        }

        string[] changed = [.. new ApprovalStore(feature).CurrentHashes(phase)
            .Where(current => !check.Bound.Contains(current))
            .Select(current => current.Name)];
        if (changed.Length > 0 || check.Bound.Count != ApprovalPhaseNames.ArtifactsOf(phase).Count)
        {
            codes.Discard(phaseName);
            return Refuse(new SessionRefusal("changed", $"the {phaseName} changed since you saw it ({string.Join(", ", changed)}); run /{CommandName} {phaseName} again"), words);
        }

        // The tasks check also reads the spec and its approval, which the code does not bind.
        if (ApprovalReadiness.Of(manifest, feature, phase) is { Ready: false } readiness)
        {
            codes.Discard(phaseName);
            return Refuse(new SessionRefusal("phase-check", readiness.Explanation), words);
        }

        ApprovalRecording recording = await new ApprovalRecorder(context, manifest.Root).RecordAsync(
            feature, phase, check.Bound, options.Flag("--sign"), options.Value("--key"), ApprovalChannel.Session, cancellationToken);
        if (!recording.Recorded)
        {
            return Refuse(new SessionRefusal("record-failed", recording.Problem!), words);
        }

        codes.Discard(phaseName);
        string artifacts = recording.ArtifactSummary.Replace('=', ' ').Replace(",", ", ", StringComparison.Ordinal);
        return SessionCommandResult.Recorded(
            $"Step 2 of 2 — approved: the {phaseName} of {feature.Id} ({artifacts}){(recording.Signed ? ", signed" : string.Empty)}.",
            $"The person approved the {phaseName} of {feature.Id} in this session; continue with the next step. "
            + $"Re-read the approval state from the engine ({EngineCommand.Linked} approve verify --feature {feature.Id} --require {phaseName}), never assume it.");
    }

    /// <summary>A refusal that ends with the command for a separate terminal, with the options the person typed (never the code).</summary>
    private SessionCommandResult Refuse(SessionRefusal refusal, string[] words)
    {
        string phase = words.Length > 0 && ApprovalPhaseNames.TryParse(words[0], out ApprovalPhase parsed) ? ApprovalPhaseNames.ToName(parsed) : "spec|tasks";
        string[] options = words.Length > 1 && SessionCommandGate.SplitCode(words[1..], ValueOptions) is { } split ? split.OptionWords : [];
        return Gate.Refuse(refusal, Gate.Terminal(phase, options));
    }
}
