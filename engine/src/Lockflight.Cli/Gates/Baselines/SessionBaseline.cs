using System.Security.Cryptography;
using System.Text;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hooks.Requests;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle.Approvals;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Gates.Baselines;

/// <summary>
/// <c>/lockflight:baseline build|tests|deps|suppressions [--path unit]</c> typed by the person in Claude Code: <c>lockflight
/// baseline</c> in two steps (<see cref="SessionCommandGate"/>). The first runs what the baseline is taken from and shows every entry it would accept,
/// with a code bound to those entries and to the state of the files (<see cref="WorkingTreeFingerprint"/>); the second, with that
/// code and only while no file changed, writes exactly those entries, without running anything again.
/// </summary>
internal sealed class SessionBaseline(CliContext context, Manifest manifest) : ISessionDecider
{
    public const string CommandName = "lockflight:baseline";

    private const string PathOption = "--path";
    private const string RepositorySubject = ".";
    private const string TreeBound = "tree";
    private const string EntriesBound = "entries";
    private const int MaxEntriesShown = 40;
    private const int HashPrefixLength = 12;

    private static readonly string[] ValueOptions = [PathOption];

    public static SessionCommandSpec Spec { get; } = new(
        CommandName, "baseline", "nothing was accepted", "accepts a baseline", "session-baseline", (context, manifest) => new SessionBaseline(context, manifest),
        [
            new("baseline-build", "baseline", _ => $"/{CommandName} build <code>"),
            new("baseline-tests", "baseline", _ => $"/{CommandName} tests <code>"),
            new("baseline-deps", "baseline", _ => $"/{CommandName} deps <code>"),
            new("baseline-suppressions", "baseline", _ => $"/{CommandName} suppressions <code>"),
        ]);

    /// <summary>The longest a first step may run: below the plugin's 600-second hook time limit, leaving time to answer.</summary>
    private static TimeSpan Budget { get; } = TimeSpan.FromSeconds(540);

    private SessionCommandGate Gate => new(context, manifest, Spec);

    public async Task<SessionCommandResult> DecideAsync(HookRequest input, CancellationToken cancellationToken)
    {
        string[] words = SessionCommandGate.Words(input);
        if (Gate.NotTypedByAPerson(input, "accepts a baseline") is SessionRefusal notTyped)
        {
            return Refuse(notTyped, words);
        }

        if (words.Length == 0 && PendingStep() is SessionCommandResult pending)
        {
            return pending;
        }

        if (words.Length == 0 || !BaselineKindNames.TryParse(words[0], out BaselineKind kind))
        {
            return Refuse(new SessionRefusal("usage", $"type /{CommandName} build, tests, deps or suppressions (optionally with --path <unit>)"), words);
        }

        if (SessionCommandGate.SplitCode(words[1..], ValueOptions) is not { } split
            || CommandOptions.Parse(split.OptionWords, ValueOptions, []) is not { Options: CommandOptions options })
        {
            return Refuse(new SessionRefusal("usage", $"/{CommandName} takes {words[0]}, then optionally --path <unit> and the code it showed you"), words);
        }

        if (UnitOf(options) is not string subject)
        {
            string units = manifest.Units.Count == 0 ? "this project has none" : $"it has {string.Join(", ", manifest.Units.Select(unit => unit.Path))}";
            return Refuse(new SessionRefusal("usage", $"--path takes a unit listed in lockflight.yaml ({units})"), words);
        }

        string slot = $"baseline-{BaselineKindNames.ToName(kind)}";
        if (Gate.UnsafeState(slot) is SessionRefusal unsafeState)
        {
            return Refuse(unsafeState, words);
        }

        return split.Code is string code
            ? await AcceptAsync(kind, slot, subject, code, words, cancellationToken)
            : await ShowAsync(kind, slot, subject, words, cancellationToken);
    }

    /// <summary>The line to type when a code waits for a kind and the person typed the command alone; null when none waits.</summary>
    private SessionCommandResult? PendingStep()
    {
        foreach (BaselineKind waitingKind in new[] { BaselineKind.Build, BaselineKind.Tests, BaselineKind.Dependencies })
        {
            string name = BaselineKindNames.ToName(waitingKind);
            if (Gate.Codes.Waiting($"baseline-{name}", IsSubject) is SessionCodeWaiting waiting)
            {
                string line = $"/{CommandName} {name}";
                return Gate.Pending(waiting, $"the {name} baseline", line, $"To see it again with a new code, type {line}{PathWords(waiting.Subject)}");
            }
        }

        return null;
    }

    /// <summary>The unit <c>--path</c> names, or <see cref="RepositorySubject"/> without it; null when it names no unit of the manifest.</summary>
    private string? UnitOf(CommandOptions options)
    {
        if (options.Value(PathOption) is not string path)
        {
            return RepositorySubject;
        }

        string unitPath = path.Replace('\\', '/').Trim('/');
        return unitPath == RepositorySubject ? RepositorySubject : manifest.Units.FirstOrDefault(unit => unit.Path == unitPath)?.Path;
    }

    private string Target(string subject) =>
        subject == RepositorySubject ? manifest.Root : Path.Combine(manifest.Root, subject.Replace('/', Path.DirectorySeparatorChar));

    private async Task<SessionCommandResult> ShowAsync(BaselineKind kind, string slot, string subject, string[] words, CancellationToken cancellationToken)
    {
        string target = Target(subject);
        if (await WorkingTreeFingerprint.OfAsync(target, cancellationToken) is not string before)
        {
            return Refuse(new SessionRefusal("not-a-repository", $"/{CommandName} binds what it shows to the state of the files, which needs a git repository"), words);
        }

        BaselineRun run = new(context, manifest);
        BaselineCapture capture;
        using (CancellationTokenSource budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            budget.CancelAfter(Budget);
            try
            {
                capture = await run.CaptureAsync(kind, target, Budget, underHook: true, budget.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Refuse(new SessionRefusal("too-slow", $"taking the baseline took longer than {Budget.TotalSeconds:0} seconds, the most a typed command may run"), words);
            }
        }

        if (capture.Entries is not IReadOnlyList<string> entries)
        {
            return Refuse(new SessionRefusal("not-taken", capture.Problem!), words);
        }

        if (await WorkingTreeFingerprint.OfAsync(target, cancellationToken) != before)
        {
            return Refuse(new SessionRefusal("changed", "files changed while the engine took the baseline; type the command again once nothing is being edited"), words);
        }

        string code = Gate.Codes.Issue(slot, subject, [(TreeBound, before), (EntriesBound, HashOf(entries))], entries);
        return SessionCommandResult.Shown(Digest(kind, capture.UnitPath, entries, run, code, subject));
    }

    private async Task<SessionCommandResult> AcceptAsync(BaselineKind kind, string slot, string subject, string code, string[] words, CancellationToken cancellationToken)
    {
        string kindName = BaselineKindNames.ToName(kind);
        string again = $"type /{CommandName} {kindName}{PathWords(subject)} to see it again with a new code";
        SessionCodes codes = Gate.Codes;
        SessionCodeCheck check = codes.Check(slot, code, IsSubject);
        if (SessionCommandGate.CodeProblem(check, "code", $"the {kindName} baseline", again) is SessionRefusal codeProblem)
        {
            return Refuse(codeProblem, words);
        }

        if (check.Subject != subject)
        {
            string shownFor = check.Subject == RepositorySubject ? "the repository" : $"unit {check.Subject}";
            return Refuse(new SessionRefusal("other-unit", $"the code was shown for {shownFor}; {again}"), words);
        }

        string? tree = await WorkingTreeFingerprint.OfAsync(Target(subject), cancellationToken);
        if (tree is null || !check.Bound.Contains((TreeBound, tree)) || !check.Bound.Contains((EntriesBound, HashOf(check.Payload))))
        {
            codes.Discard(slot);
            return Refuse(new SessionRefusal("changed", $"files changed since the engine took the baseline you saw, so it may no longer be what the build or tests give; run /{CommandName} {kindName} again"), words);
        }

        BaselineRun run = new(context, manifest);
        string? unitPath = subject == RepositorySubject ? null : subject;
        string saved = await run.SaveAsync(kind, unitPath, check.Payload, cancellationToken);
        codes.Discard(slot);
        return SessionCommandResult.Recorded(
            $"Step 2 of 2 — accepted: {saved}. CI reads baselines from the base branch: commit it and merge it in its own pull request first.",
            $"The person accepted the {kindName} baseline in this session ({run.RelativePath(kind, unitPath)}). It counts in CI only once it is on the base branch, "
            + "so it belongs in its own pull request. Re-run the gates to see its effect, never assume it.");
    }

    private bool IsSubject(string subject) => subject == RepositorySubject || manifest.Units.Any(unit => unit.Path == subject);

    private static string Digest(BaselineKind kind, string? unitPath, IReadOnlyList<string> entries, BaselineRun run, string code, string subject)
    {
        string kindName = BaselineKindNames.ToName(kind);
        string what = unitPath is null ? "the repository" : $"unit {unitPath}";
        string source = kind switch
        {
            BaselineKind.Build => "ran a full build",
            BaselineKind.Tests => "ran the tests",
            _ => "listed the vulnerable packages",
        };
        IReadOnlyList<string> current = run.Current(kind, unitPath);
        string[] added = [.. entries.Except(current, StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        string[] dropped = [.. current.Except(entries, StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        StringBuilder digest = new();
        digest.AppendLine(SessionCommandGate.Opening(
            "nothing has been accepted yet", $"Check the {Count(kind, entries)} {BaselineKindNames.Entries(kind, Count(kind, entries))} you are accepting as the {kindName} baseline of {what}; the engine {source}"));
        digest.AppendLine($"  File: {run.RelativePath(kind, unitPath)} ({(current.Count == 0 ? "new" : $"replaces {Count(kind, current)} accepted now")}); entries {HashOf(entries)[..HashPrefixLength]}");
        digest.AppendLine($"  Compared with the current baseline: {added.Length} new, {dropped.Length} no longer listed");
        digest.AppendLine($"  Accepted ({Count(kind, entries)}):");
        string[] shown = [.. Describe(kind, entries)];
        foreach (string line in shown.Take(MaxEntriesShown))
        {
            digest.AppendLine($"    - {line}");
        }

        if (shown.Length > MaxEntriesShown)
        {
            digest.AppendLine($"    … and {shown.Length - MaxEntriesShown} more (the code binds every one of them)");
        }

        digest.AppendLine("  CI reads baselines from the base branch: merge this one in its own pull request before the change that needs it.");
        digest.Append(SessionCommandGate.Closing($"/{CommandName} {kindName} {code}{PathWords(subject)}", "no file changes from the state the engine took the baseline in"));
        return digest.ToString();
    }

    /// <summary>One line per distinct entry, in order; a build warning seen several times once, with its count.</summary>
    private static IEnumerable<string> Describe(BaselineKind kind, IReadOnlyList<string> entries) =>
        entries.GroupBy(entry => entry, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group =>
        {
            string text = kind == BaselineKind.Build && group.Key.Split('|', 3) is [string diagnostic, string file, string message]
                ? $"{diagnostic} {file}: {message}"
                : group.Key;
            return group.Count() > 1 ? $"{text} (×{group.Count()})" : text;
        });

    /// <summary>Accepted build warnings count each occurrence; tests and advisories count once.</summary>
    private static int Count(BaselineKind kind, IReadOnlyList<string> entries) =>
        kind == BaselineKind.Build ? entries.Count : entries.Distinct(StringComparer.Ordinal).Count();

    private static string HashOf(IReadOnlyList<string> entries) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', entries))));

    private static string PathWords(string subject) => subject == RepositorySubject ? string.Empty : $" {PathOption} {subject}";

    /// <summary>A refusal that ends with the command for a separate terminal, with the options the person typed (never the code).</summary>
    private SessionCommandResult Refuse(SessionRefusal refusal, string[] words)
    {
        string kind = words.Length > 0 && BaselineKindNames.TryParse(words[0], out BaselineKind parsed) ? BaselineKindNames.ToName(parsed) : "build|tests|deps|suppressions";
        string[] options = words.Length > 1 && SessionCommandGate.SplitCode(words[1..], ValueOptions) is { } split ? split.OptionWords : [];
        return Gate.Refuse(refusal, Gate.Terminal(kind, options));
    }
}
