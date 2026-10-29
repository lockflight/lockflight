using Lockflight.Cli.Config;
using Lockflight.Cli.Hooks;
using Lockflight.Cli.Linking;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Hosting;

/// <summary>Parses the command line and dispatches to a command.</summary>
internal static class CliApplication
{
    private static readonly string Usage = $"""
        lockflight — gated delivery engine for Claude Code agents

        Usage: lockflight <command> [options]   (in a project, from its root: .lockflight/bin/lockflight <command> [options])

        Setup
          init [--profile lite|standard] [--ci pr|manual|none] [--ci-platform github|azure|bitbucket|gitlab]
               [--quality] [--architecture-tests] [--no-link] [--no-sandbox]
                                                        onboard: manifest, tool pin, settings, CI job, link; --ci:
                                                        the job runs on pull requests, only by hand, or is not written;
                                                        --ci-platform: the CI service the job is for (default github);
                                                        --quality: recommended analyzers and code metrics rules (an
                                                        existing file gets a proposal); --architecture-tests: a layer
                                                        dependency test project
          doctor [--self-test]                          readiness report with fixes; --self-test: plant a violation
                                                        before the guard, a human-only command, the linked engine
                                                        check, the test gate and red in a scratch project and
                                                        report each as caught or missed (seconds, no build;
                                                        --canaries is a deprecated name for it)
          docs check                                    references in CLAUDE.md, rules, skills, the constitution
                                                        and docs/ that no longer resolve (links, anchors, deleted or
                                                        moved paths, engine and plugin commands) and instruction
                                                        files over Claude Code's size targets (read only)
          facts [--json]                                deterministic facts for the crew's skills: protected paths,
                                                        gate steps and modes, analyzer, banned-symbol and code
                                                        metrics settings, architecture tests, the ledger (read only)
          statusline                                    Claude Code status line: Levi and the last verdict
          update [--source <feed>]                      you: install the newest Lockflight (from its checkout or a
                                                        package feed), re-pin this project
          adopt plan                                    inventory an existing Claude Code / Spec Kit setup into
                                                        .lockflight/adopt/plan.md (read only)
          workspace sync [--check]                      vendor each unit repository's CI policy (lockflight.unit.yaml,
                                                        engine pin, baselines, workflow); --check reports drift
          link                                          copy the pinned engine to .lockflight/bin/ for the hooks
          version                                       print the engine version

        Gates
          gate run [--profile edit|stop|pr] [--path <dir>] [--unit <path>[,…]] [--base <ref>] [--result <file>] [--full]
                                                        run the quality gates (default profile: stop, which builds
                                                        and tests the projects changed since its last PASS
                                                        and their dependents;
                                                        --full: every project; --base also scopes format and
                                                        coverage to the branch's changes; at a workspace root,
                                                        every changed unit or the --unit ones)
          hook <event>                                  Claude Code hook decision, hook JSON on stdin; events:
                                                        {string.Join(", ", HookEvents())}

        Lifecycle (agent)
          phase check specify|tasks [--feature <id>]    deterministic phase-exit checks
          redgreen start|red|green|status [--feature <id>]
                                                        test-first cycles: baseline, accepted red, green;
                                                        red --verbose also prints each test's deciding
                                                        stack frame and the production call found
          trace [--feature <id>]                        requirement → red/green tests matrix
          blocked validate <file>                       check a lockflight.blocked/v1 report (what stopped and what
                                                        a person must do) against its contract (read only)
          review [--feature <id>] [--base <ref>] [--model <name>] [--budget <usd>]
                                                        review bound to evidence (defaults: the target branch,
                                                        the manifest's review model and budget, else sonnet, 3.00);
                                                        open findings carry forward until fixed or dismissed
          review status [--feature <id>]                review runs, open, fixed and dismissed findings, and
                                                        the summary for the pull request (read only)
          propose <path> (--from <file> | --delete) [<path> …]
                                                        a protected change as one patch per file in
                                                        .lockflight/local/proposals/ for a person to review and
                                                        git apply; applies nothing itself

        Human decisions (approve, redgreen reset|amend, review dismiss, baseline, adopt apply|undo and update refuse to run inside a
        Claude Code session, a `!` command included: run them in a separate terminal window)
          approve spec|tasks [--feature <id>]           approve the feature's artifacts (a local record; the
                                                        approval of record is the pull request's review);
                                                        in Claude Code, type /lockflight:approve spec|tasks instead
          redgreen reset [--feature <id>]               discard the feature's red/green evidence
          redgreen amend <test> --reason "<text>" [--feature <id>]
                                                        let one test of the open red cycle change: its file is
                                                        writable until the next red re-checks it
          review dismiss <finding-id> --reason "<text>" [--feature <id>]
                                                        close an open review finding, recording who, when and why
          baseline build|tests|deps [--path <dir>]      accept current warnings / failing tests / advisories
          baseline suppressions                         accept the suppressions the branch adds (file and rule)
          adopt apply [--speckit-preset <dir>] [--skip-speckit] [--no-sandbox]
                                                        apply the reviewed adoption plan (backs up first)
          adopt undo [--backup <id>] [--force]          revert what apply changed; files changed since are kept
                                                        unless --force

        CI
          approve verify (--feature <id> | --base <ref>) [--require spec,tasks]
                                                        check approval records and artifact hashes;
                                                        exactly one of --feature or --base (never the active feature)
          gate run --profile pr [--base <ref>] [--result <file>]
                                                        stop steps plus policy, suppressions, references, EF
                                                        model changes, patch coverage, dependencies, secrets
          ci [--head <sha>] [--base <sha>] [--branch <name>] [--platform github|bitbucket|gitlab|azure|local]
             [--event pr|push|merge-group] [--evidence <dir>] [--shell <path>] [--no-review-expected] [--plan]
                                                        judge a pull request's commits with the target branch's
                                                        policy: the red commits (Lockflight-Red trailer), the change
                                                        class and each check, into evidence.json; --plan prints the
                                                        steps without building (local: HEAD against the target)

        Exit codes
          0  PASS; also a gate run whose steps were all N/A (e.g. edit with no changed C# lines)
          1  FAIL
          2  blocked: a hook decision, or a human-only command run by the agent
          3  ERROR: the engine or a tool it ran broke; not a verdict about the code
          4  configuration: invalid command line (unknown option, missing value), manifest or target
          5  NOT-EARNED: nothing to judge (e.g. zero tests)

        Any command with --help or -h prints this text.
        """;

    public static async Task<ExitCode> RunAsync(string[] args, CliContext context, CancellationToken cancellationToken)
    {
        // Every command prints names and text from the project; none of it may reach the terminal as control sequences.
        context = context.ForTerminal();
        if (args.Length == 0 || args.Any(arg => arg is "-h" or "--help"))
        {
            await context.Output.WriteLineAsync(Usage);
            return args.Length == 0 ? ExitCode.Config : ExitCode.Pass;
        }

        if (HumanOnlyCommands.IsReserved(args) && HumanOnlyCommands.RunsUnderAgent(context.Environment))
        {
            await context.Error.WriteLineAsync(HumanOnlyCommands.Refusal(args, ProjectDirectory(context), EngineFor(context)));
            // Exit 2, the verdict the PreToolUse guard gives the same command line: a refusal to the agent, not a failed check.
            return ExitCode.HookBlock;
        }

        // `--version` is the conventional spelling of `version`; an unknown command is reported as it was typed.
        string[] words = args is ["--version", ..] ? ["version", .. args[1..]] : args;
        if (CommandRegistry.Match(words) is not CommandDescriptor command || !command.AcceptsRest(words.Length - command.Words.Count))
        {
            return await ReportUnknownAsync(args, context);
        }

        try
        {
            return await command.Create(context).RunAsync(new CommandArguments(command, words[command.Words.Count..]), cancellationToken);
        }
        catch (ExecutableNotFoundException exception)
        {
            await context.Error.WriteLineAsync($"lockflight: {exception.Message}");
            return ExitCode.Error;
        }
        catch (Exception exception)
        {
            // A crash must never look like success to the caller.
            await context.Error.WriteLineAsync($"lockflight: internal error: {exception.Message}");
            return ExitCode.Error;
        }
    }

    private static string ProjectDirectory(CliContext context) =>
        ManifestLocator.Discover(context.WorkingDirectory) is { Manifest: Manifest manifest } ? manifest.Root : context.WorkingDirectory;

    /// <summary>The project's linked engine when there is one: it runs whatever the install mode; else how this engine was started.</summary>
    private static string EngineFor(CliContext context) =>
        File.Exists(LinkCommand.BinaryPath(ProjectDirectory(context)))
            ? EngineCommand.Linked
            : EngineCommand.Name;

    private static IEnumerable<string> HookEvents() =>
        Enum.GetValues<HookEvent>().Where(hookEvent => hookEvent != HookEvent.Unknown).Select(HookEventNames.ToName);

    private static async Task<ExitCode> ReportUnknownAsync(string[] args, CliContext context)
    {
        await context.Error.WriteLineAsync($"lockflight: unknown command '{string.Join(' ', args)}'. Run 'lockflight --help'.");
        return ExitCode.Config;
    }
}
