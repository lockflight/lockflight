using Lockflight.Cli.Config;
using Lockflight.Cli.Hooks;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Linking;

namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>
/// <c>/lockflight:approve spec|tasks [--feature id] [--sign [--key file]]</c> typed by the person in Claude Code, seen by the
/// <c>UserPromptExpansion</c> hook (which fires only for a typed prompt, never for the agent's own Skill call). Two steps: without a
/// code the engine shows the person — and not the agent — what would be approved with a one-time code; with that code it records
/// the approval exactly as <c>lockflight approve</c> does. It is a guardrail: it keeps the agent, headless sessions and scheduled
/// turns from approving, and binds the approval to the content shown; it cannot stop something that types into the person's
/// terminal. Signed approvals verified in CI are the boundary.
/// </summary>
internal sealed class SessionApproval(CliContext context, Manifest manifest)
{
    public const string CommandName = "lockflight:approve";

    private const string PluginSource = "plugin";
    private const string InteractiveEntrypoint = "cli";
    private const string Attended = "1";

    private static readonly string[] ValueOptions = ["--feature", "--key"];
    private static readonly string[] Flags = ["--sign"];

    /// <summary>Any spelling of the command: a differently cased one is refused rather than passed to the agent unchecked.</summary>
    public static bool Handles(HookInput input) => string.Equals(input.CommandName, CommandName, StringComparison.OrdinalIgnoreCase);

    public async Task<SessionApprovalResult> DecideAsync(HookInput input, CancellationToken cancellationToken)
    {
        if (!Handles(input))
        {
            return SessionApprovalResult.Ignored;
        }

        string[] words = (input.CommandArguments ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (input.CommandSource != PluginSource)
        {
            return Refuse("not-plugin", $"/{CommandName} here comes from '{input.CommandSource ?? "(unknown)"}', not from the Lockflight plugin", words);
        }

        if (UnattendedReason() is string unattended)
        {
            return Refuse("unattended", $"/{CommandName} records an approval only when you type it in an interactive Claude Code session, and this one {unattended}", words);
        }

        if (words.Length == 0 || !ApprovalPhaseNames.TryParse(words[0], out ApprovalPhase phase))
        {
            return Refuse("usage", $"type /{CommandName} spec or /{CommandName} tasks (optionally with --feature <id> and --sign)", words);
        }

        if (SplitCode(words[1..]) is not { } split
            || CommandOptions.Parse(split.OptionWords, ValueOptions, Flags) is not { Options: CommandOptions options })
        {
            return Refuse("usage", $"/{CommandName} takes {words[0]}, then optionally --feature <id>, --sign, --key <file> and the code it showed you", words);
        }

        if (options.Value("--feature") is string requested && !FeatureLocator.IsPlainId(requested))
        {
            return Refuse("usage", $"--feature takes a feature ID, the name of a directory under {FeatureLocator.SpecsDirectory}/, not a path", words);
        }

        if (Codes().StateProblem(phase) is string problem)
        {
            return Refuse("unsafe-state", $"{problem}; only the engine writes there, so remove it and type the command again", words);
        }

        return split.Code is string code
            ? await RecordAsync(phase, code, options, words, cancellationToken)
            : Show(phase, options, words);
    }

    /// <summary>
    /// Why the session is not one a person attends; null when it is. Claude Code marks an interactive session with the entrypoint
    /// <c>cli</c> and attended <c>1</c>; a headless one (<c>claude -p</c>, also when the agent starts it) is <c>sdk-cli</c> and
    /// <c>0</c>. Anything missing or unknown counts as unattended.
    /// </summary>
    private string? UnattendedReason()
    {
        string? entrypoint = context.Environment.Get(EnvironmentVariables.ClaudeCodeEntrypoint);
        string? attended = context.Environment.Get(EnvironmentVariables.ClaudeCodeSessionAttended);
        if (entrypoint != InteractiveEntrypoint)
        {
            return $"is not interactive ({EnvironmentVariables.ClaudeCodeEntrypoint}={entrypoint ?? "unset"})";
        }

        return attended == Attended ? null : $"is not attended ({EnvironmentVariables.ClaudeCodeSessionAttended}={attended ?? "unset"})";
    }

    /// <summary>The code (the one word that is neither an option nor an option's value) and the remaining option words.</summary>
    private static (string? Code, string[] OptionWords)? SplitCode(string[] words)
    {
        string? code = null;
        List<string> optionWords = [];
        for (int index = 0; index < words.Length; index++)
        {
            if (ValueOptions.Contains(words[index]) && index + 1 < words.Length)
            {
                optionWords.Add(words[index]);
                optionWords.Add(words[++index]);
            }
            else if (words[index].StartsWith('-'))
            {
                optionWords.Add(words[index]);
            }
            else if (code is null)
            {
                code = words[index];
            }
            else
            {
                return null;
            }
        }

        return (code, [.. optionWords]);
    }

    private SessionApprovalResult Show(ApprovalPhase phase, CommandOptions options, string[] words)
    {
        FeatureLookup lookup = FeatureLocator.Resolve(manifest.Root, options.Value("--feature"));
        if (lookup.Feature is not Feature feature)
        {
            return Refuse("no-feature", lookup.Error!, words);
        }

        string primary = Path.Combine(feature.SpecDirectory, ApprovalPhaseNames.ArtifactsOf(phase)[0]);
        if (!File.Exists(primary))
        {
            return Refuse("nothing-to-approve", $"{Path.GetRelativePath(manifest.Root, primary)} does not exist; nothing to approve", words);
        }

        ApprovalStore store = new(feature);
        (string Name, string Hash)[] artifacts = [.. store.CurrentHashes(phase)];
        string code = Codes().Issue(feature.Id, phase, artifacts);
        string typedOptions = (options.Flag("--sign") ? " --sign" : string.Empty)
            + (options.Value("--key") is string key ? $" --key {key}" : string.Empty);
        return SessionApprovalResult.Shown(ApprovalDigest.Render(feature, phase, artifacts, store.Check(phase), code, typedOptions));
    }

    private async Task<SessionApprovalResult> RecordAsync(ApprovalPhase phase, string code, CommandOptions options, string[] words, CancellationToken cancellationToken)
    {
        string phaseName = ApprovalPhaseNames.ToName(phase);
        string again = $"type /{CommandName} {phaseName} to see it again with a new code";
        ApprovalCodes codes = Codes();
        ApprovalCodeCheck check = codes.Check(phase, code);
        switch (check.State)
        {
            case ApprovalCodeState.NoneWaiting:
                return Refuse("no-code", $"no approval code is waiting for the {phaseName} (it was used, or never shown); {again}", words);
            case ApprovalCodeState.Expired:
                return Refuse("code-expired", $"the code expired (codes last {ApprovalCodes.Lifetime.TotalMinutes:0} minutes); {again}", words);
            case ApprovalCodeState.WrongCode:
                return Refuse("wrong-code", $"that is not the code shown with the {phaseName}; type the code exactly, or {again}", words);
        }

        if (options.Value("--feature") is string requested && requested != check.FeatureId)
        {
            return Refuse("other-feature", $"the code was shown for feature {check.FeatureId}, not {requested}; {again}", words);
        }

        if (FeatureLocator.Resolve(manifest.Root, check.FeatureId).Feature is not Feature feature)
        {
            codes.Discard(phase);
            return Refuse("no-feature", $"feature {check.FeatureId} no longer exists under {FeatureLocator.SpecsDirectory}/", words);
        }

        string[] changed = [.. new ApprovalStore(feature).CurrentHashes(phase)
            .Where(current => !check.Artifacts.Contains(current))
            .Select(current => current.Name)];
        if (changed.Length > 0 || check.Artifacts.Count != ApprovalPhaseNames.ArtifactsOf(phase).Count)
        {
            codes.Discard(phase);
            return Refuse("changed", $"the {phaseName} changed since you saw it ({string.Join(", ", changed)}); run /{CommandName} {phaseName} again", words);
        }

        ApprovalRecording recording = await new ApprovalRecorder(context, manifest.Root).RecordAsync(
            feature, phase, check.Artifacts, options.Flag("--sign"), options.Value("--key"), ApprovalChannel.Session, cancellationToken);
        if (!recording.Recorded)
        {
            return Refuse("record-failed", recording.Problem!, words);
        }

        codes.Discard(phase);
        string artifacts = recording.ArtifactSummary.Replace('=', ' ').Replace(",", ", ", StringComparison.Ordinal);
        return SessionApprovalResult.Recorded(
            $"Approved {phaseName} of {feature.Id}: {artifacts}{(recording.Signed ? " (signed)" : string.Empty)}",
            $"The person approved the {phaseName} of {feature.Id} in this session; continue with the next step. "
            + $"Re-read the approval state from the engine ({EngineCommand.Linked} approve verify --feature {feature.Id} --require {phaseName}), never assume it.");
    }

    private ApprovalCodes Codes() => new(manifest.Root, context.Time, context.Random);

    /// <summary>A refusal that ends with the command for a separate terminal, with the options the person typed (never the code).</summary>
    private SessionApprovalResult Refuse(string code, string reason, string[] words)
    {
        string phase = words.Length > 0 && ApprovalPhaseNames.TryParse(words[0], out ApprovalPhase parsed) ? ApprovalPhaseNames.ToName(parsed) : "spec|tasks";
        string[] options = words.Length > 1 && SplitCode(words[1..]) is { } split ? split.OptionWords : [];
        string engine = File.Exists(LinkCommand.BinaryPath(manifest.Root)) ? EngineCommand.Linked : EngineCommand.Name;
        string command = $"cd {ShellQuoting.Quote(manifest.Root)} && {engine} approve {phase}{string.Concat(options.Select(option => " " + ShellQuoting.QuoteIfNeeded(option)))}";
        return SessionApprovalResult.Refused(
            $"session-approval:{code}",
            $"lockflight: nothing was approved: {reason}.\nOr run it {HumanOnlyCommands.WhereToRun}:\n  {command}");
    }
}
