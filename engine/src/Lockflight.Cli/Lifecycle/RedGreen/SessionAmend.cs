using System.Text;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hooks.Requests;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle.Approvals;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// <c>/lockflight:amend &lt;test&gt; [--feature id] --reason "…"</c> typed by the person in Claude Code: <c>lockflight redgreen
/// amend</c> in two steps (<see cref="SessionCommandGate"/>). The first shows the test, the file it would unlock and the other tests
/// in that file, with a code bound to the red/green record's exact bytes and to the reason given; the second,
/// <c>/lockflight:amend &lt;test&gt; &lt;code&gt;</c>, records the amend as the terminal command does while the record and the files
/// are unchanged. The reason is the rest of the line after <c>--reason</c>, so it comes last.
/// </summary>
internal sealed class SessionAmend(CliContext context, Manifest manifest) : ISessionDecider
{
    public const string CommandName = "lockflight:amend";

    private const string Slot = "amend";
    private const string ReasonOption = "--reason";
    private const string UnknownPerson = "unknown";

    private static readonly string[] ValueOptions = ["--feature"];

    public static SessionCommandSpec Spec { get; } = new(
        CommandName, "redgreen amend", "nothing was amended", "amends a locked test", "session-amend", (context, manifest) => new SessionAmend(context, manifest),
        [new(Slot, "amend", waiting => $"/{CommandName} {(waiting.Payload.Count > 0 ? TraceMatrix.ShortName(waiting.Payload[0].Split(':')[^1]) : "<test>")} <code>")]);

    private SessionCommandGate Gate => new(context, manifest, Spec);

    public async Task<SessionCommandResult> DecideAsync(HookRequest input, CancellationToken cancellationToken)
    {
        string[] words = SessionCommandGate.Words(input);
        (string[] head, string? reason) = SplitReason(words);
        if (Gate.NotTypedByAPerson(input, "records an amend") is SessionRefusal notTyped)
        {
            return Refuse(notTyped, head, reason);
        }

        if (head.Length == 0 && reason is null && Gate.Codes.Waiting(Slot, FeatureLocator.IsPlainId) is SessionCodeWaiting waiting && waiting.Payload.Count > 0)
        {
            string waitingTest = TraceMatrix.ShortName(waiting.Payload[0].Split(':')[^1]);
            return Gate.Pending(waiting, $"the amend of {waitingTest} in {waiting.Subject}", $"/{CommandName} {waitingTest}", $"To see it again with a new code, type /{CommandName} {waitingTest} --reason <why>");
        }

        if (head is not [string test, .. string[] rest] || test.StartsWith('-')
            || SessionCommandGate.SplitCode(rest, ValueOptions) is not { } split
            || CommandOptions.Parse(split.OptionWords, ValueOptions, []) is not { Options: CommandOptions options }
            || (split.Code is null) == (reason is null))
        {
            return Refuse(
                new SessionRefusal("usage", $"type /{CommandName} <Class.Method> --reason <why the test must change> (optionally --feature <id> before --reason), then /{CommandName} <Class.Method> <code>"),
                head,
                reason);
        }

        if (options.Value("--feature") is string requested && !FeatureLocator.IsPlainId(requested))
        {
            return Refuse(new SessionRefusal("usage", $"--feature takes a feature ID, the name of a directory under {FeatureLocator.SpecsDirectory}/, not a path"), head, reason);
        }

        if (reason is { Length: 0 })
        {
            return Refuse(new SessionRefusal("usage", $"{ReasonOption} needs the reason the test must change"), head, reason);
        }

        if (Gate.UnsafeState(Slot) is SessionRefusal unsafeState)
        {
            return Refuse(unsafeState, head, reason);
        }

        return split.Code is string code
            ? await AmendAsync(test, code, options, head, cancellationToken)
            : await ShowAsync(test, reason!, options, head, cancellationToken);
    }

    /// <summary>The words before <c>--reason</c>, and the reason (the rest of the line, without surrounding quotes); null without one.</summary>
    private static (string[] Head, string? Reason) SplitReason(string[] words)
    {
        int at = Array.IndexOf(words, ReasonOption);
        if (at < 0)
        {
            return (words, null);
        }

        string reason = string.Join(' ', words[(at + 1)..]).Trim();
        if (reason.Length >= 2 && (reason[0], reason[^1]) is ('"', '"') or ('\'', '\''))
        {
            reason = reason[1..^1].Trim();
        }

        return (words[..at], reason);
    }

    private async Task<SessionCommandResult> ShowAsync(string test, string reason, CommandOptions options, string[] head, CancellationToken cancellationToken)
    {
        FeatureLookup lookup = FeatureLocator.Resolve(manifest.Root, options.Value("--feature"));
        if (lookup.Feature is not Feature feature)
        {
            return Refuse(new SessionRefusal("no-feature", lookup.Error!), head, reason);
        }

        RedGreenSnapshot snapshot = RedGreenSnapshot.Of(feature);
        AmendTargetLookup found = RedGreenAmends.Find(manifest.Root, snapshot.Record, test);
        if (found.Target is not AmendTarget target)
        {
            return Refuse(new SessionRefusal("no-target", snapshot.Unreadable is string unreadable ? $"the red/green record cannot be read ({unreadable})" : found.Problem!), head, reason);
        }

        string person = await Git.ConfigValueAsync(manifest.Root, "user.email", cancellationToken) ?? UnknownPerson;
        string code = Gate.Codes.Issue(Slot, feature.Id, [(RedGreenSnapshot.BoundName, snapshot.Hash)], [target.Test.Key, reason, .. target.Files]);
        return SessionCommandResult.Shown(Digest(feature, target, reason, person, test, code, options.Value("--feature")));
    }

    private async Task<SessionCommandResult> AmendAsync(string test, string code, CommandOptions options, string[] head, CancellationToken cancellationToken)
    {
        string again = $"type /{CommandName} {test} --reason <why> to see it again with a new code";
        SessionCodes codes = Gate.Codes;
        SessionCodeCheck check = codes.Check(Slot, code, FeatureLocator.IsPlainId);
        if (SessionCommandGate.CodeProblem(check, "code", "the amend", again) is SessionRefusal codeProblem)
        {
            return Refuse(codeProblem, head, reason: null);
        }

        if (options.Value("--feature") is string requested && requested != check.Subject)
        {
            return Refuse(new SessionRefusal("other-feature", $"the code was shown for feature {check.Subject}, not {requested}; {again}"), head, reason: null);
        }

        if (check.Payload.Count < 2)
        {
            codes.Discard(Slot);
            return Refuse(new SessionRefusal("no-code", $"no code is waiting for the amend (it was used, or never shown); {again}"), head, reason: null);
        }

        if (FeatureLocator.Resolve(manifest.Root, check.Subject).Feature is not Feature feature)
        {
            codes.Discard(Slot);
            return Refuse(new SessionRefusal("no-feature", $"feature {check.Subject} no longer exists under {FeatureLocator.SpecsDirectory}/"), head, reason: null);
        }

        (string testKey, string reason, string[] files) = (check.Payload[0], check.Payload[1], [.. check.Payload.Skip(2)]);
        RedGreenSnapshot snapshot = RedGreenSnapshot.Of(feature);
        AmendTargetLookup found = RedGreenAmends.Find(manifest.Root, snapshot.Record, test);
        if (found.Target is not AmendTarget target || target.Test.Key != testKey)
        {
            codes.Discard(Slot);
            return Refuse(new SessionRefusal("other-test", $"the code was shown for {testKey}, and '{test}' names {(found.Target is AmendTarget other ? other.Test.Key : "no test that can be amended now")}; {again}"), head, reason: null);
        }

        if (!check.Bound.Contains((RedGreenSnapshot.BoundName, snapshot.Hash)) || !target.Files.SequenceEqual(files, StringComparer.Ordinal))
        {
            codes.Discard(Slot);
            return Refuse(new SessionRefusal("changed", $"the red/green evidence of {feature.Id} or the files declaring {target.Test.MethodFilter} changed since you saw them; {again}"), head, reason: null);
        }

        string person = await Git.ConfigValueAsync(manifest.Root, "user.email", cancellationToken) ?? UnknownPerson;
        RedGreenSession session = new(context, manifest, feature, TestWorkspaces.For(manifest, feature, context.Time));
        await AmendStep.RecordAsync(session, snapshot.Record!, target, person, reason, cancellationToken);
        codes.Discard(Slot);
        return SessionCommandResult.Recorded(
            $"Step 2 of 2 — amended: {target.Test.MethodFilter} in red cycle {target.Cycle.Number} of {feature.Id}. {RedGreenAmends.Unlocks(target)}",
            $"The person amended {target.Test.MethodFilter} in this session: {string.Join(", ", target.Files)} may now be changed, and only as the reason says ({reason}). "
            + $"Change only that test, then run '{EngineCommand.Linked} redgreen red': it must fail in production code again. "
            + $"Re-read the state from the engine ({EngineCommand.Linked} redgreen status --feature {feature.Id}), never assume it.");
    }

    private static string Digest(Feature feature, AmendTarget target, string reason, string person, string test, string code, string? requestedFeature)
    {
        StringBuilder digest = new();
        digest.AppendLine(SessionCommandGate.Opening("nothing has been amended yet", $"Check the test of red cycle {target.Cycle.Number} of {feature.Id} you are unlocking"));
        digest.AppendLine($"  Test: {target.Test.MethodFilter}");
        digest.AppendLine($"  Unlocks: {string.Join(", ", target.Files)}, writable until the next 'redgreen red' re-checks the test");
        digest.AppendLine(target.SharedWith.Count == 0
            ? "  No other test of the cycle is declared in that file."
            : $"  Also writable, in the same file: {string.Join(", ", target.SharedWith)}");
        digest.AppendLine($"  Reason: {reason}");
        digest.AppendLine($"  Recorded as: {person}");
        digest.AppendLine("  The amended test must fail in production code again (RED_STUB or RED_ASSERT) before the cycle can go green.");
        string featureOption = requestedFeature is null ? string.Empty : $" --feature {requestedFeature}";
        digest.Append(SessionCommandGate.Closing($"/{CommandName} {test} {code}{featureOption}", "the red/green evidence stays as shown"));
        return digest.ToString();
    }

    /// <summary>A refusal that ends with the command for a separate terminal, with what the person typed (never the code).</summary>
    private SessionCommandResult Refuse(SessionRefusal refusal, string[] head, string? reason)
    {
        string test = head.Length > 0 && !head[0].StartsWith('-') ? ShellQuoting.QuoteIfNeeded(head[0]) : "<test>";
        string[] options = head.Length > 1 && SessionCommandGate.SplitCode(head[1..], ValueOptions) is { } split ? split.OptionWords : [];
        string because = reason is { Length: > 0 } ? ShellQuoting.Quote(reason) : "\"<why>\"";
        return Gate.Refuse(refusal, $"{Gate.Terminal(test, options)} {ReasonOption} {because}");
    }
}
