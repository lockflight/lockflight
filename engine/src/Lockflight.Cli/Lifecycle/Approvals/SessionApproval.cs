using Lockflight.Cli.Config;
using Lockflight.Cli.Hooks.Requests;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;

namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>
/// <c>/lockflight:approve [spec|tasks] [--feature id]</c> typed by the person in Claude Code (who may type it:
/// <see cref="SessionCommandGate"/>). One step: it runs the matching phase check (<see cref="ApprovalReadiness"/>) and records the
/// approval exactly as <c>lockflight approve</c> does, bound to the artifacts' hashes at that moment, but only while those hashes are
/// the ones the person could last see in this session (<see cref="SeenArtifacts"/>: when the agent's last turn ended, or as this
/// command last showed them). Otherwise it shows the person the content as it is now, remembers it as seen, and records nothing: typing the command again
/// approves exactly that content. The record is the agent loop's guardrail; the approval of record is the platform's review of the
/// pull request.
/// </summary>
internal sealed class SessionApproval(CliContext context, Manifest manifest) : ISessionDecider
{
    public const string CommandName = "lockflight:approve";

    private const string FeatureOption = "--feature";

    private static readonly string[] ValueOptions = [FeatureOption];
    private static readonly string[] Flags = [];

    public static SessionCommandSpec Spec { get; } = new(
        CommandName, "approve", "nothing was approved", "approves", "session-approval", (context, manifest) => new SessionApproval(context, manifest), []);

    private SessionCommandGate Gate => new(context, manifest, Spec);

    public async Task<SessionCommandResult> DecideAsync(HookRequest input, CancellationToken cancellationToken)
    {
        string[] words = SessionCommandGate.Words(input);
        if (Gate.NotTypedByAPerson(input, "records an approval") is SessionRefusal notTyped)
        {
            return Refuse(notTyped, words);
        }

        bool phaseTyped = words.Length > 0 && !words[0].StartsWith('-');
        ApprovalPhase phase = default;
        if (phaseTyped && !ApprovalPhaseNames.TryParse(words[0], out phase))
        {
            return Refuse(Usage(), words);
        }

        if (SessionCommandGate.SplitCode(phaseTyped ? words[1..] : words, ValueOptions) is not { } split
            || CommandOptions.Parse(split.OptionWords, ValueOptions, Flags) is not { Options: CommandOptions options }
            || (split.Code is string extra && !LooksLikeAnOldCode(extra)))
        {
            return Refuse(Usage(), words);
        }

        string? requested = options.Value(FeatureOption);
        if (requested is not null && !FeatureLocator.IsPlainId(requested))
        {
            return Refuse(new SessionRefusal("usage", $"{FeatureOption} takes a feature ID, the name of a directory under {FeatureLocator.SpecsDirectory}/, not a path"), words);
        }

        if (!phaseTyped && PhaseWaiting(requested, out phase) is SessionRefusal choice)
        {
            return Refuse(choice, words);
        }

        if (new SeenArtifacts(manifest.Root).StateProblem() is string problem)
        {
            return Refuse(new SessionRefusal("unsafe-state", $"{problem}; only the engine writes there, so remove it and type the command again"), words, phase);
        }

        bool oldCode = split.Code is not null;
        SessionCommandResult result = await ApproveAsync(phase, requested, input.Session.Id, oldCode, words, cancellationToken);
        return oldCode && result.Outcome != SessionCommandOutcome.Recorded
            ? Refuse(new SessionRefusal("usage", $"codes are no longer needed; type {Line(phase, requested)} without one"), words, phase)
            : result;
    }

    /// <summary>
    /// The phase the command typed without one is for (Claude Code's command menu sends the command as soon as it is picked): the one
    /// that waits for an approval (<see cref="WaitingApproval"/>). Otherwise the answer that lists both phases, their state and the
    /// line to type.
    /// </summary>
    private SessionRefusal? PhaseWaiting(string? requested, out ApprovalPhase phase)
    {
        phase = default;
        string choose = $"type /{CommandName} spec or /{CommandName} tasks";
        FeatureLookup lookup = FeatureLocator.Resolve(manifest.Root, requested);
        if (lookup.Feature is not Feature feature)
        {
            return new SessionRefusal(requested is null ? "usage" : "no-feature", requested is null ? $"{lookup.Error}; {choose} with {FeatureOption} <id>" : lookup.Error!);
        }

        if (WaitingApproval.Of(feature) is ApprovalPhase waiting)
        {
            phase = waiting;
            return null;
        }

        ApprovalStore store = new(feature);
        bool specWritten = File.Exists(feature.SpecPath);
        bool tasksWritten = File.Exists(Path.Combine(feature.SpecDirectory, ArtifactSnapshot.TasksName));
        return new SessionRefusal(
            "no-phase-waiting",
            $"no approval is waiting for {feature.Id} (spec: {StateOf(store.Check(ApprovalPhase.Spec), specWritten, "spec.md")}; "
            + $"tasks: {StateOf(store.Check(ApprovalPhase.Tasks), tasksWritten, ArtifactSnapshot.TasksName)}); to approve one anyway, {choose}");
    }

    /// <summary>
    /// Records the approval when the phase check passes and the artifacts are as the person could last see them; else shows them as
    /// they are now and remembers that (unless <paramref name="oldCode"/>: an old-style second step is not taken for a first look).
    /// </summary>
    private async Task<SessionCommandResult> ApproveAsync(ApprovalPhase phase, string? requested, string? sessionId, bool oldCode, string[] words, CancellationToken cancellationToken)
    {
        FeatureLookup lookup = FeatureLocator.Resolve(manifest.Root, requested);
        if (lookup.Feature is not Feature feature)
        {
            return Refuse(new SessionRefusal("no-feature", lookup.Error!), words, phase);
        }

        // Each artifact is read once: the record binds the hashes of exactly the bytes the message describes.
        ApprovalStore store = new(feature);
        IReadOnlyList<ArtifactSnapshot> snapshot = store.Snapshot(phase);
        if (snapshot[0].Content is null)
        {
            string primary = Path.Combine(feature.SpecDirectory, snapshot[0].Name);
            return Refuse(new SessionRefusal("nothing-to-approve", $"{Path.GetRelativePath(manifest.Root, primary)} does not exist; nothing to approve"), words, phase);
        }

        if (ApprovalReadiness.Of(manifest, feature, phase) is { Ready: false } readiness)
        {
            return Refuse(new SessionRefusal("phase-check", readiness.Explanation), words, phase);
        }

        (string Name, string Hash)[] artifacts = [.. snapshot.Select(artifact => (artifact.Name, artifact.Hash))];
        string phaseName = ApprovalPhaseNames.ToName(phase);

        // The phase check read the files again: content swapped in meanwhile is neither checked nor seen.
        if (!store.CurrentHashes(phase).SequenceEqual(artifacts))
        {
            return SessionCommandResult.Answered(
                $"{Spec.RefusalPrefix}:changed", $"lockflight: {Spec.NothingDone}: the {phaseName} changed while it was checked; type {Line(phase, requested)} again.\n{SessionCommandGate.NotAnError}");
        }

        SeenArtifacts seen = new(manifest.Root);
        SeenCheck? check = seen.Compare(feature, sessionId, artifacts);
        ApprovalCheck before = store.Check(phase, snapshot);
        if (check is { Changed.Count: 0 })
        {
            await new ApprovalRecorder(context, manifest.Root).RecordAsync(feature, phase, artifacts, ApprovalChannel.Session, cancellationToken);
            string note = oldCode ? $" (codes are no longer needed: /{CommandName} alone approves)" : string.Empty;
            string replaces = before.State == ApprovalState.Stale
                ? $"\n  Replaces an approval that went stale ({string.Join(", ", before.ChangedArtifacts)} changed since)"
                : string.Empty;

            // Claude Code shows the system message as it is, so text from the spec is made printable here.
            return SessionCommandResult.Recorded(
                TerminalText.Clean($"Approved: the {phaseName} of {feature.Id}{note}.\n{ApprovalDigest.Render(phase, snapshot, current: null)}{replaces}"),
                $"The person approved the {phaseName} of {feature.Id} in this session; continue with the next step. "
                + $"Re-read the approval state from the engine ({EngineCommand.Linked} approve verify --feature {feature.Id} --require {phaseName}), never assume it.");
        }

        if (oldCode)
        {
            return Refuse(Usage(), words, phase);
        }

        seen.Remember(feature, sessionId, artifacts, SeenWhen.Shown);
        string why = check switch
        {
            null => $"there is no record of the {phaseName} from the end of the agent's last turn (the turn was interrupted, or none ended since Lockflight was updated)",
            { When: SeenWhen.TurnEnded } => $"the {phaseName} changed since the agent's last turn ended ({string.Join(", ", check.Changed)})",
            _ => $"the {phaseName} changed since it was last shown to you ({string.Join(", ", check.Changed)})",
        };
        string line = Line(phase, requested);
        return SessionCommandResult.Answered(
            $"{Spec.RefusalPrefix}:{(check is null ? "not-seen" : "changed")}",
            $"lockflight: not approved yet (not an error): {why}. Lockflight approves only what is unchanged since the agent's turn ended "
            + $"or since it was last shown to you, so review it below, then type {line} again to approve exactly this content.\n"
            + $"Check the {phaseName} of {feature.Id} (the engine read this, the agent did not write it):\n"
            + $"{ApprovalDigest.Render(phase, snapshot, before)}\n"
            + "If it changes before you type it, you are shown it again. The agent does not see this message.");
    }

    private static SessionRefusal Usage() =>
        new("usage", $"type /{CommandName}, or /{CommandName} spec or /{CommandName} tasks, optionally with {FeatureOption} <id>");

    /// <summary>The line that approves <paramref name="phase"/>, with the feature when the person named one.</summary>
    private static string Line(ApprovalPhase phase, string? requested) =>
        $"/{CommandName} {ApprovalPhaseNames.ToName(phase)}{(requested is null ? string.Empty : $" {FeatureOption} {requested}")}";

    /// <summary>A word shaped like the one-time code the command asked for before it took one step (six letters or digits of its alphabet).</summary>
    private static bool LooksLikeAnOldCode(string word) =>
        word.Length == SessionCodes.CodeLength && word.ToLowerInvariant().All(letter => SessionCodes.Alphabet.Contains(letter, StringComparison.Ordinal));

    private static string StateOf(ApprovalCheck check, bool written, string file) => !written ? $"{file} not written yet" : check.State switch
    {
        ApprovalState.Valid => $"approved by {check.ApprovedBy}",
        ApprovalState.Stale => $"approval stale ({string.Join(", ", check.ChangedArtifacts)} changed)",
        ApprovalState.Invalid => "approval record not valid",
        _ => "not approved",
    };

    private SessionCommandResult Refuse(SessionRefusal refusal, string[] words) =>
        Refuse(refusal, words, words.Length > 0 && ApprovalPhaseNames.TryParse(words[0], out ApprovalPhase parsed) ? parsed : null);

    /// <summary>A refusal that ends with the command for a separate terminal, for <paramref name="phase"/> and the options the person typed.</summary>
    private SessionCommandResult Refuse(SessionRefusal refusal, string[] words, ApprovalPhase? phase)
    {
        string[] rest = words.Length > 0 && !words[0].StartsWith('-') ? words[1..] : words;
        string[] options = SessionCommandGate.SplitCode(rest, ValueOptions) is { } split ? split.OptionWords : [];
        return Gate.Refuse(refusal, Gate.Terminal(phase is ApprovalPhase known ? ApprovalPhaseNames.ToName(known) : "spec|tasks", options));
    }
}
