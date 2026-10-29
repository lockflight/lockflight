using System.Globalization;
using System.Text;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hooks.Requests;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle.Approvals;
using Lockflight.Cli.Lifecycle.RedGreen.Snapshots;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// <c>/lockflight:reset [--feature id]</c> typed by the person in Claude Code: <c>lockflight redgreen reset</c> in two steps
/// (<see cref="SessionCommandGate"/>). The first shows the evidence that would be discarded, with a code bound to the record's exact
/// bytes; the second, with that code and only while the record is unchanged, discards it as the terminal command does.
/// </summary>
internal sealed class SessionReset(CliContext context, Manifest manifest) : ISessionDecider
{
    public const string CommandName = "lockflight:reset";

    private const string Slot = "reset";

    private static readonly string[] ValueOptions = ["--feature"];

    public static SessionCommandSpec Spec { get; } = new(
        CommandName, "redgreen reset", "nothing was reset", "discards red/green evidence", "session-reset", (context, manifest) => new SessionReset(context, manifest),
        [new(Slot, "reset", _ => $"/{CommandName} <code>")]);

    private SessionCommandGate Gate => new(context, manifest, Spec);

    public async Task<SessionCommandResult> DecideAsync(HookRequest input, CancellationToken cancellationToken)
    {
        string[] words = SessionCommandGate.Words(input);
        if (Gate.NotTypedByAPerson(input, "discards red/green evidence") is SessionRefusal notTyped)
        {
            return Refuse(notTyped, words);
        }

        if (SessionCommandGate.SplitCode(words, ValueOptions) is not { } split
            || CommandOptions.Parse(split.OptionWords, ValueOptions, []) is not { Options: CommandOptions options })
        {
            return Refuse(new SessionRefusal("usage", $"/{CommandName} takes optionally --feature <id>, and the code it showed you"), words);
        }

        if (options.Value("--feature") is string requested && !FeatureLocator.IsPlainId(requested))
        {
            return Refuse(new SessionRefusal("usage", $"--feature takes a feature ID, the name of a directory under {FeatureLocator.SpecsDirectory}/, not a path"), words);
        }

        if (Gate.UnsafeState(Slot) is SessionRefusal unsafeState)
        {
            return Refuse(unsafeState, words);
        }

        return split.Code is string code
            ? await ResetAsync(code, options, words, cancellationToken)
            : Show(options, words);
    }

    private SessionCommandResult Show(CommandOptions options, string[] words)
    {
        FeatureLookup lookup = FeatureLocator.Resolve(manifest.Root, options.Value("--feature"));
        if (lookup.Feature is not Feature feature)
        {
            return Refuse(new SessionRefusal("no-feature", lookup.Error!), words);
        }

        RedGreenSnapshot snapshot = RedGreenSnapshot.Of(feature);
        if (!snapshot.Exists)
        {
            return Refuse(new SessionRefusal("nothing-to-reset", $"{feature.Id} has no red/green evidence to discard"), words);
        }

        string code = Gate.Codes.Issue(Slot, feature.Id, [(RedGreenSnapshot.BoundName, snapshot.Hash)]);
        return SessionCommandResult.Shown(Digest(feature, snapshot, code, options.Value("--feature")));
    }

    private async Task<SessionCommandResult> ResetAsync(string code, CommandOptions options, string[] words, CancellationToken cancellationToken)
    {
        string again = $"type /{CommandName} to see it again with a new code";
        SessionCodes codes = Gate.Codes;
        SessionCodeCheck check = codes.Check(Slot, code, FeatureLocator.IsPlainId);
        if (SessionCommandGate.CodeProblem(check, "code", "the reset", again) is SessionRefusal codeProblem)
        {
            return Refuse(codeProblem, words);
        }

        if (options.Value("--feature") is string requested && requested != check.Subject)
        {
            return Refuse(new SessionRefusal("other-feature", $"the code was shown for feature {check.Subject}, not {requested}; {again}"), words);
        }

        if (FeatureLocator.Resolve(manifest.Root, check.Subject).Feature is not Feature feature)
        {
            codes.Discard(Slot);
            return Refuse(new SessionRefusal("no-feature", $"feature {check.Subject} no longer exists under {FeatureLocator.SpecsDirectory}/"), words);
        }

        RedGreenSnapshot snapshot = RedGreenSnapshot.Of(feature);
        if (!check.Bound.Contains((RedGreenSnapshot.BoundName, snapshot.Hash)) || !snapshot.Exists)
        {
            codes.Discard(Slot);
            return Refuse(new SessionRefusal("changed", $"the red/green evidence of {feature.Id} changed since you saw it; run /{CommandName} again"), words);
        }

        string refs = await RedGreenCommand.DiscardAsync(new RedGreenSession(context, manifest, feature, TestWorkspaces.For(manifest, feature, context.Time)), cancellationToken);
        codes.Discard(Slot);
        return SessionCommandResult.Recorded(
            $"Step 2 of 2 — reset: the red/green evidence of {feature.Id} is discarded, and the test projects are unlocked for this feature.{refs}",
            $"The person discarded the red/green evidence of {feature.Id} in this session. Re-read the state from the engine "
            + $"({EngineCommand.Linked} redgreen status --feature {feature.Id}), never assume it; red/green starts again with '{EngineCommand.Linked} redgreen start'.");
    }

    private static string Digest(Feature feature, RedGreenSnapshot snapshot, string code, string? requestedFeature)
    {
        StringBuilder digest = new();
        digest.AppendLine(SessionCommandGate.Opening("nothing has been reset yet", $"Check the red/green evidence of {feature.Id} you are discarding"));
        digest.AppendLine($"  {RedGreenRecord.FileName} {snapshot.Hash[..12]} (deleted)");
        if (snapshot.Record is RedGreenRecord record)
        {
            digest.AppendLine($"  Baseline: {record.BaselineTests.Count} tests at commit {RedGreenSession.Short(record.BaselineCommit)}, {record.BaselineAt.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)}");
            foreach (RedGreenCycle cycle in record.Cycles)
            {
                string state = cycle.IsOpen ? "red, test projects locked" : "green";
                digest.AppendLine($"  Cycle {cycle.Number}: {cycle.Tests.Count} tests, {state}");
            }

            foreach (string amend in RedGreenAmends.Lines(record))
            {
                digest.AppendLine($"  Amended: {amend}");
            }

            foreach (string reference in RedSnapshots.RefsOf(feature.Id, record))
            {
                digest.AppendLine($"  Red-time snapshot {reference} (deleted)");
            }

            digest.AppendLine(record.OpenCycle is null
                ? "  Afterwards: red/green starts again from a new baseline."
                : "  Afterwards: the test projects unlock for this feature, and red/green starts again from a new baseline.");
        }
        else
        {
            digest.AppendLine($"  The record cannot be read ({snapshot.Unreadable ?? "no baseline in it"}); it locks the conventional test folders until it is discarded.");
        }

        string featureOption = requestedFeature is null ? string.Empty : $" --feature {requestedFeature}";
        digest.Append(SessionCommandGate.Closing($"/{CommandName} {code}{featureOption}", "the record stays as shown"));
        return digest.ToString();
    }

    /// <summary>A refusal that ends with the command for a separate terminal, with the options the person typed (never the code).</summary>
    private SessionCommandResult Refuse(SessionRefusal refusal, string[] words)
    {
        string[] options = SessionCommandGate.SplitCode(words, ValueOptions) is { } split ? split.OptionWords : [];
        return Gate.Refuse(refusal, Gate.Terminal(string.Empty, options));
    }
}
