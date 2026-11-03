using Lockflight.Cli.Config;
using Lockflight.Cli.Hooks.Requests;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;

namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>
/// <c>/lockflight:approve spec|tasks [--feature id]</c> typed by the person in Claude Code (the two steps and
/// their checks: <see cref="SessionCommandGate"/>). Without a code the engine shows the person what would be approved with a one-time
/// code bound to the artifacts' hashes; with that code it records the approval exactly as <c>lockflight approve</c> does. Both steps
/// first run the matching phase check and refuse while it fails (<see cref="ApprovalReadiness"/>). The record is the
/// agent loop's guardrail; the approval of record is the platform's review of the pull request.
/// </summary>
internal sealed class SessionApproval(CliContext context, Manifest manifest) : ISessionDecider
{
    public const string CommandName = "lockflight:approve";

    private const string FeatureOption = "--feature";

    /// <summary>The fewest hex digits taken for the start of an artifact's hash rather than a mistyped code.</summary>
    private const int MinimumHashStart = 4;

    private static readonly string[] ValueOptions = [FeatureOption];
    private static readonly string[] Flags = [];

    public static SessionCommandSpec Spec { get; } = new(
        CommandName, "approve", "nothing was approved", "approves", "session-approval", (context, manifest) => new SessionApproval(context, manifest),
        [new("spec", "approval", _ => $"/{CommandName} spec <code>"), new("tasks", "approval", _ => $"/{CommandName} tasks <code>")]);

    private SessionCommandGate Gate => new(context, manifest, Spec);

    public async Task<SessionCommandResult> DecideAsync(HookRequest input, CancellationToken cancellationToken)
    {
        string[] words = SessionCommandGate.Words(input);
        if (Gate.NotTypedByAPerson(input, "records an approval") is SessionRefusal notTyped)
        {
            return Refuse(notTyped, words);
        }

        if (words.Length == 0)
        {
            if (PendingStep() is SessionCommandResult pending)
            {
                return pending;
            }

            if (PhaseWaiting(out string waitingPhase) is SessionRefusal choice)
            {
                return Refuse(choice, words);
            }

            words = [waitingPhase];
        }

        if (!ApprovalPhaseNames.TryParse(words[0], out ApprovalPhase phase))
        {
            return Refuse(new SessionRefusal("usage", $"type /{CommandName} spec or /{CommandName} tasks (optionally with {FeatureOption} <id>)"), words);
        }

        if (SessionCommandGate.SplitCode(words[1..], ValueOptions) is not { } split
            || CommandOptions.Parse(split.OptionWords, ValueOptions, Flags) is not { Options: CommandOptions options })
        {
            return Refuse(new SessionRefusal("usage", $"/{CommandName} takes {words[0]}, then optionally {FeatureOption} <id> and the code it showed you{StillWaiting(phase)}"), words);
        }

        if (options.Value(FeatureOption) is string requested && !FeatureLocator.IsPlainId(requested))
        {
            return Refuse(new SessionRefusal("usage", $"{FeatureOption} takes a feature ID, the name of a directory under {FeatureLocator.SpecsDirectory}/, not a path"), words);
        }

        if (Gate.UnsafeState(ApprovalPhaseNames.ToName(phase)) is SessionRefusal unsafeState)
        {
            return Refuse(unsafeState, words);
        }

        return split.Code is string code
            ? await RecordAsync(phase, code, options, words, cancellationToken)
            : Show(phase, options.Value(FeatureOption), words);
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

    /// <summary>
    /// The phase step 1 starts for when the person typed the command alone and no code waits (Claude Code's command menu sends the
    /// command as soon as it is picked): the spec of the active feature while it is written and its approval is missing, stale or
    /// not valid; else its tasks while they are written, the spec is approved and the tasks are not. Otherwise the answer that lists
    /// both phases, their state and the line to type, and <paramref name="phaseName"/> is empty.
    /// </summary>
    private SessionRefusal? PhaseWaiting(out string phaseName)
    {
        phaseName = string.Empty;
        string choose = $"type /{CommandName} spec or /{CommandName} tasks";
        FeatureLookup lookup = FeatureLocator.Resolve(manifest.Root, requested: null);
        if (lookup.Feature is not Feature feature)
        {
            return new SessionRefusal("usage", $"{lookup.Error}; {choose} with {FeatureOption} <id>");
        }

        ApprovalStore store = new(feature);
        ApprovalCheck spec = store.Check(ApprovalPhase.Spec);
        ApprovalCheck tasks = store.Check(ApprovalPhase.Tasks);
        bool specWritten = File.Exists(feature.SpecPath);
        bool tasksWritten = File.Exists(Path.Combine(feature.SpecDirectory, ArtifactSnapshot.TasksName));
        if (specWritten && spec.State != ApprovalState.Valid)
        {
            phaseName = ApprovalPhaseNames.ToName(ApprovalPhase.Spec);
            return null;
        }

        if (spec.State == ApprovalState.Valid && tasksWritten && tasks.State != ApprovalState.Valid)
        {
            phaseName = ApprovalPhaseNames.ToName(ApprovalPhase.Tasks);
            return null;
        }

        return new SessionRefusal(
            "no-phase-waiting",
            $"no approval is waiting for {feature.Id} (spec: {StateOf(spec, specWritten, "spec.md")}; tasks: {StateOf(tasks, tasksWritten, ArtifactSnapshot.TasksName)}); "
            + $"to approve one anyway, {choose}");
    }

    private SessionCommandResult Show(ApprovalPhase phase, string? requested, string[] words) =>
        TryShow(phase, requested, out string digest) is SessionRefusal refusal ? Refuse(refusal, words) : SessionCommandResult.Shown(digest);

    /// <summary>Step 1: issues a code bound to the artifacts' hashes and renders the digest; the refusal instead when there is nothing to show.</summary>
    private SessionRefusal? TryShow(ApprovalPhase phase, string? requested, out string digest)
    {
        digest = string.Empty;
        FeatureLookup lookup = FeatureLocator.Resolve(manifest.Root, requested);
        if (lookup.Feature is not Feature feature)
        {
            return new SessionRefusal("no-feature", lookup.Error!);
        }

        // Each artifact is read once: the code binds the hashes of exactly the bytes the digest describes.
        ApprovalStore store = new(feature);
        IReadOnlyList<ArtifactSnapshot> snapshot = store.Snapshot(phase);
        if (snapshot[0].Content is null)
        {
            string primary = Path.Combine(feature.SpecDirectory, snapshot[0].Name);
            return new SessionRefusal("nothing-to-approve", $"{Path.GetRelativePath(manifest.Root, primary)} does not exist; nothing to approve");
        }

        if (ApprovalReadiness.Of(manifest, feature, phase) is { Ready: false } readiness)
        {
            return new SessionRefusal("phase-check", readiness.Explanation);
        }

        (string Name, string Hash)[] artifacts = [.. snapshot.Select(artifact => (artifact.Name, artifact.Hash))];
        string code = Gate.Codes.Issue(ApprovalPhaseNames.ToName(phase), feature.Id, artifacts);
        digest = ApprovalDigest.Render(feature, phase, snapshot, store.Check(phase, snapshot), code);
        return null;
    }

    /// <summary>
    /// A second step whose code no longer works because it expired or the files changed: the refusal, then step 1 again with a new
    /// code, as typing the command again would show. When step 1 cannot show one, the refusal says why.
    /// </summary>
    private SessionCommandResult ShowAgain(SessionRefusal refusal, ApprovalPhase phase, string? requested, string[] words) =>
        TryShow(phase, requested, out string digest) is SessionRefusal notShown
            ? Refuse(refusal with { Reason = $"{refusal.Reason}, and no new code was shown:\n{notShown.Reason}" }, words)
            : Gate.RefuseAndShowAgain(refusal, $"the {ApprovalPhaseNames.ToName(phase)}", digest);

    private async Task<SessionCommandResult> RecordAsync(ApprovalPhase phase, string code, CommandOptions options, string[] words, CancellationToken cancellationToken)
    {
        string phaseName = ApprovalPhaseNames.ToName(phase);
        string again = $"type /{CommandName} {phaseName} to see it again with a new code";
        string? requested = options.Value(FeatureOption);
        SessionCodes codes = Gate.Codes;
        SessionCodeWaiting? shown = codes.Waiting(phaseName, FeatureLocator.IsPlainId);
        SessionCodeCheck check = codes.Check(phaseName, code, FeatureLocator.IsPlainId);
        if (check.State == SessionCodeState.Expired)
        {
            return ShowAgain(new SessionRefusal("code-expired", SessionCommandGate.ExpiredReason), phase, requested ?? check.Subject, words);
        }

        if (SessionCommandGate.CodeProblem(check, "approval code", $"the {phaseName}", again, HashStartOf(code, shown)) is SessionRefusal codeProblem)
        {
            return Refuse(codeProblem, words);
        }

        if (requested is not null && requested != check.Subject)
        {
            return Refuse(
                new SessionRefusal("other-feature", $"the code was shown for feature {check.Subject}, not {requested}; it still works for {check.Subject} {SessionCommandGate.Until(check.ExpiresAt)}; {again}"),
                words);
        }

        if (FeatureLocator.Resolve(manifest.Root, check.Subject).Feature is not Feature feature)
        {
            codes.Discard(phaseName);
            return Refuse(new SessionRefusal("no-feature", $"feature {check.Subject} no longer exists under {FeatureLocator.SpecsDirectory}/, so that code was discarded and no longer works"), words);
        }

        string[] changed = [.. new ApprovalStore(feature).CurrentHashes(phase)
            .Where(current => !check.Bound.Contains(current))
            .Select(current => current.Name)];
        if (changed.Length > 0 || check.Bound.Count != ApprovalPhaseNames.ArtifactsOf(phase).Count)
        {
            codes.Discard(phaseName);
            return ShowAgain(
                new SessionRefusal("changed", $"the {phaseName} changed since you saw it ({(changed.Length == 0 ? "its set of files" : string.Join(", ", changed))}), so that code was discarded and no longer works"), phase, check.Subject, words);
        }

        // The tasks check also reads the spec and its approval, which the code does not bind.
        if (ApprovalReadiness.Of(manifest, feature, phase) is { Ready: false } readiness)
        {
            codes.Discard(phaseName);
            return Refuse(new SessionRefusal("phase-check", $"{readiness.Explanation}\nThe code you typed was discarded and no longer works; {again}"), words);
        }

        ApprovalRecording recording = await new ApprovalRecorder(context, manifest.Root).RecordAsync(
            feature, phase, check.Bound, ApprovalChannel.Session, cancellationToken);
        codes.Discard(phaseName);
        string artifacts = recording.ArtifactSummary.Replace('=', ' ').Replace(",", ", ", StringComparison.Ordinal);
        return SessionCommandResult.Recorded(
            $"Step 2 of 2 — approved: the {phaseName} of {feature.Id} ({artifacts}).",
            $"The person approved the {phaseName} of {feature.Id} in this session; continue with the next step. "
            + $"Re-read the approval state from the engine ({EngineCommand.Linked} approve verify --feature {feature.Id} --require {phaseName}), never assume it.");
    }

    /// <summary>For a refusal of a second step that left the code alone: that the code waiting for the phase still works; empty when none waits.</summary>
    private string StillWaiting(ApprovalPhase phase)
    {
        string name = ApprovalPhaseNames.ToName(phase);
        return Gate.Codes.Waiting(name, FeatureLocator.IsPlainId) is SessionCodeWaiting waiting
            ? $"; the code waiting for the {name} of {waiting.Subject} still works {SessionCommandGate.Until(waiting.ExpiresAt)}"
            : string.Empty;
    }

    /// <summary>
    /// <c>3bd45 is the start of spec.md's hash</c> when the typed word (at least <see cref="MinimumHashStart"/> hex digits) starts a
    /// hash the digest showed with the waiting code; null otherwise. Only said after the word was checked, and counted, as a code.
    /// </summary>
    private static string? HashStartOf(string typed, SessionCodeWaiting? shown)
    {
        string word = typed.Trim().ToLowerInvariant();
        if (shown is null || word.Length < MinimumHashStart || !word.All(char.IsAsciiHexDigitLower))
        {
            return null;
        }

        string? artifact = shown.Bound.Where(bound => bound.Hash.StartsWith(word, StringComparison.Ordinal)).Select(bound => bound.Name).FirstOrDefault();
        return artifact is null ? null : $"{word} is the start of {artifact}'s hash";
    }

    private static string StateOf(ApprovalCheck check, bool written, string file) => !written ? $"{file} not written yet" : check.State switch
    {
        ApprovalState.Valid => $"approved by {check.ApprovedBy}",
        ApprovalState.Stale => $"approval stale ({string.Join(", ", check.ChangedArtifacts)} changed)",
        ApprovalState.Invalid => "approval record not valid",
        _ => "not approved",
    };

    /// <summary>A refusal that ends with the command for a separate terminal, with the options the person typed (never the code).</summary>
    private SessionCommandResult Refuse(SessionRefusal refusal, string[] words)
    {
        string phase = words.Length > 0 && ApprovalPhaseNames.TryParse(words[0], out ApprovalPhase parsed) ? ApprovalPhaseNames.ToName(parsed) : "spec|tasks";
        string[] options = words.Length > 1 && SessionCommandGate.SplitCode(words[1..], ValueOptions) is { } split ? split.OptionWords : [];
        return Gate.Refuse(refusal, Gate.Terminal(phase, options));
    }
}
