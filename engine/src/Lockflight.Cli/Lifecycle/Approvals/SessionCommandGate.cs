using Lockflight.Cli.Config;
using Lockflight.Cli.Hooks.Requests;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Linking;

namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>
/// What every in-session human command (<c>/lockflight:approve</c>, <c>/lockflight:baseline</c>, <c>/lockflight:reset</c>,
/// <c>/lockflight:amend</c>) checks the same way. The <c>UserPromptExpansion</c> hook fires only for a prompt that expands a slash
/// command, never for the agent's own Skill call. The command must come from the Lockflight plugin and be typed in an interactive
/// session a person attends. The commands that discard or unlock evidence run in two steps: without a code the engine shows the
/// person, and not the agent, what would be recorded, with a one-time code bound to that state (<see cref="SessionCodes"/>); with
/// the code, and only while the state is unchanged, it records the decision as the terminal command does. <c>/lockflight:approve</c>
/// takes one step (<see cref="SessionApproval"/>). A refusal ends with the command for a separate terminal. A guardrail: it keeps
/// the agent, headless sessions and scheduled turns from deciding, and binds the decision to what was shown; it cannot stop
/// something that types into the person's terminal.
/// </summary>
internal sealed class SessionCommandGate(CliContext context, Manifest manifest, SessionCommandSpec spec)
{
    /// <summary>Claude Code labels every stopped prompt "blocked by hook"; an answer that says what to type is not a failure.</summary>
    public const string NotAnError = "This is not an error: Claude Code labels any stopped command 'blocked by hook'.";

    /// <summary>
    /// The first line of every digest. Claude Code shows the digest under "UserPromptExpansion operation blocked by hook", so the
    /// line says before anything else that this is not an error and what to do.
    /// </summary>
    public const string StepOneHeadline = "Step 1 of 2 — not an error: review below, then type the line with the code.";

    /// <summary>The label of the digest line that holds the code, which refusals point back to.</summary>
    public const string CodeLabel = "Your code";

    private const string PluginSource = "plugin";

    private const string InteractiveEntrypoint = "cli";
    private const string Attended = "1";

    /// <summary>
    /// Refusals that stop something other than the person's own typing (a command from another source, an unattended session, a
    /// tampered state store): guard blocks. Every other refusal is an answer to what the person typed.
    /// </summary>
    private static readonly HashSet<string> Blocking = ["not-plugin", "unattended", "unsafe-state"];

    public SessionCodes Codes => new(manifest.Root, context.Time, context.Random);

    /// <summary>Any spelling of the command: a differently cased one is refused rather than passed to the agent unchecked.</summary>
    public static bool Names(HookRequest input, SessionCommandSpec spec) => string.Equals(input.Prompt?.Name, spec.Name, StringComparison.OrdinalIgnoreCase);

    /// <summary>The words the person typed after the command, split on white space.</summary>
    public static string[] Words(HookRequest input) => (input.Prompt?.Arguments ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Why the command cannot run here: it comes from another source than the plugin, or the session is not one a person attends.
    /// Null when it can. <paramref name="whatItDoes"/> completes "/lockflight:x … only when you type it" (<c>records an approval</c>).
    /// </summary>
    public SessionRefusal? NotTypedByAPerson(HookRequest input, string whatItDoes)
    {
        if (input.Prompt?.Source != PluginSource)
        {
            return new SessionRefusal("not-plugin", $"/{spec.Name} here comes from '{input.Prompt?.Source ?? "(unknown)"}', not from the Lockflight plugin");
        }

        return UnattendedReason(context.Environment) is string unattended
            ? new SessionRefusal("unattended", $"/{spec.Name} {whatItDoes} only when you type it in an interactive Claude Code session, and this one {unattended}")
            : null;
    }

    /// <summary>Why the slot's waiting code cannot be kept safely; null when it can.</summary>
    public SessionRefusal? UnsafeState(string slot) =>
        Codes.StateProblem(slot) is string problem
            ? new SessionRefusal("unsafe-state", $"{problem}; only the engine writes there, so remove it and type the command again")
            : null;

    /// <summary>
    /// Why a typed code does not let the second step go on (none waiting, expired, wrong, too many wrong); null when it matched.
    /// <paramref name="codeName"/> names the waiting code (<c>approval code</c>), <paramref name="shownWith"/> what it was shown with
    /// (<c>the spec</c>), and <paramref name="again"/> says how to see it again.
    /// </summary>
    /// <remarks>
    /// Every refusal says whether the waiting code still works. <paramref name="notTheCode"/>, when set, says what the typed word was
    /// instead (<c>3bd45 is the start of spec.md's hash</c>) in place of "that is not the code".
    /// </remarks>
    public static SessionRefusal? CodeProblem(SessionCodeCheck check, string codeName, string shownWith, string again, string? notTheCode = null) => check.State switch
    {
        SessionCodeState.NoneWaiting => new SessionRefusal("no-code", $"no {codeName} is waiting for {shownWith} (it was used, or never shown), so there is no code to type now; {again}"),
        SessionCodeState.Expired => new SessionRefusal("code-expired", $"{ExpiredReason}; {again}"),
        SessionCodeState.WrongCode => new SessionRefusal(
            "wrong-code",
            $"{NotTheCode(notTheCode, shownWith)}; {StillWorks(check.ExpiresAt)} (it is discarded after {check.AttemptsLeft} more wrong code{(check.AttemptsLeft == 1 ? string.Empty : "s")}): type it exactly, or {again}"),
        SessionCodeState.TooManyAttempts => new SessionRefusal(
            "too-many-attempts", $"{NotTheCode(notTheCode, shownWith)}, and after {SessionCodes.MaxAttempts} wrong codes that code was discarded and no longer works; {again}"),
        _ => null,
    };

    /// <summary>Why an expired code was refused: it no longer works.</summary>
    public static string ExpiredReason => $"the code expired and no longer works (codes last {SessionCodes.Lifetime.TotalMinutes:0} minutes)";

    /// <summary>That the waiting code is still valid, and until when.</summary>
    public static string StillWorks(DateTimeOffset? expiresAt) => $"that code still works {Until(expiresAt)}";

    /// <summary><c>until 09:10 UTC</c>; <c>for now</c> when the expiry is not known.</summary>
    public static string Until(DateTimeOffset? expiresAt) => expiresAt is DateTimeOffset at ? $"until {at.ToUniversalTime():HH:mm} UTC" : "for now";

    /// <summary>
    /// The code (the one word that is neither an option nor the value of one of <paramref name="valueOptions"/>) and the remaining
    /// option words; null when more than one word could be the code.
    /// </summary>
    public static (string? Code, string[] OptionWords)? SplitCode(IReadOnlyList<string> words, IReadOnlyCollection<string> valueOptions)
    {
        string? code = null;
        List<string> optionWords = [];
        for (int index = 0; index < words.Count; index++)
        {
            if (valueOptions.Contains(words[index]) && index + 1 < words.Count)
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

    /// <summary>
    /// The first lines of every digest: <see cref="StepOneHeadline"/>, then that nothing is recorded yet (<paramref name="nothingYet"/>:
    /// <c>Nothing has been approved yet</c>) and what to check.
    /// </summary>
    public static string Opening(string nothingYet, string check) => $"{StepOneHeadline}\n{nothingYet}. {check} (the engine read this, the agent did not write it):";

    /// <summary>
    /// The closing lines of every digest: the code on its own labelled line with the line to type, then what the code binds. The
    /// label keeps the code apart from the hashes the digest shows above it.
    /// </summary>
    public static string Closing(string code, string line, string bindsWhat) =>
        $"{CodeLabel}: {code} — type within {SessionCodes.Lifetime.TotalMinutes:0} minutes: {line}\n"
        + $"The code works once, and only while {bindsWhat}. The agent does not see this message.";

    /// <summary>
    /// The answer to an incomplete second step (no verb, or no code) while a code waits: the line to type. Only a hash of the code
    /// is kept, so the line names it as the code shown with the digest.
    /// </summary>
    public SessionCommandResult Pending(SessionCodeWaiting waiting, string shownFor, string line, string again) =>
        SessionCommandResult.Answered(
            $"{spec.RefusalPrefix}:incomplete",
            $"lockflight: {spec.NothingDone} yet: a code is waiting for {shownFor}, valid until {waiting.ExpiresAt.ToUniversalTime():HH:mm} UTC.\n"
            + $"Step 2 of 2 — type: {line} <the code shown with it>\n{again}.\n{NotAnError}");

    /// <summary>A refusal that ends with the command for a separate terminal (<paramref name="terminalArguments"/> after the engine), never with the code.</summary>
    public SessionCommandResult Refuse(SessionRefusal refusal, string terminalArguments)
    {
        string engine = File.Exists(LinkCommand.BinaryPath(manifest.Root)) ? EngineCommand.Linked : EngineCommand.Name;
        string code = $"{spec.RefusalPrefix}:{refusal.Code}";
        string message = $"lockflight: {spec.NothingDone}: {refusal.Reason}.\nOr {HumanOnlyCommands.OpenATerminalAndRun($"cd {ShellQuoting.Quote(manifest.Root)} && {engine} {terminalArguments}")}.";
        return Blocking.Contains(refusal.Code) ? SessionCommandResult.Refused(code, message) : SessionCommandResult.Answered(code, message);
    }

    /// <summary>
    /// The terminal command line: <see cref="SessionCommandSpec.TerminalCommand"/>, then <paramref name="literal"/> as it is (a verb,
    /// or a placeholder such as <c>build|tests|deps</c>), then the words the person typed, each quoted when needed.
    /// </summary>
    public string Terminal(string literal, IEnumerable<string> words) =>
        spec.TerminalCommand + (literal.Length == 0 ? string.Empty : " " + literal) + string.Concat(words.Select(word => " " + ShellQuoting.QuoteIfNeeded(word)));

    private static string NotTheCode(string? notTheCode, string shownWith) =>
        notTheCode is null ? $"that is not the code shown with {shownWith}" : $"{notTheCode}, not the code; the code is on the '{CodeLabel}' line";

    /// <summary>
    /// Why the session is not one a person attends; null when it is. Claude Code marks an interactive session with the entrypoint
    /// <c>cli</c> and attended <c>1</c>; a headless one (<c>claude -p</c>, also when the agent starts it) is <c>sdk-cli</c> and
    /// <c>0</c>. Anything missing or unknown counts as unattended.
    /// </summary>
    public static string? UnattendedReason(EnvironmentReader environment)
    {
        string? entrypoint = environment.Get(EnvironmentVariables.ClaudeCodeEntrypoint);
        string? attended = environment.Get(EnvironmentVariables.ClaudeCodeSessionAttended);
        if (entrypoint != InteractiveEntrypoint)
        {
            return $"is not interactive ({EnvironmentVariables.ClaudeCodeEntrypoint}={entrypoint ?? "unset"})";
        }

        return attended == Attended ? null : $"is not attended ({EnvironmentVariables.ClaudeCodeSessionAttended}={attended ?? "unset"})";
    }
}
