using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Gates.Baselines;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Lifecycle.Approvals;
using Lockflight.Cli.Lifecycle.RedGreen.Snapshots;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// <c>lockflight redgreen start|red|green|status|reset [--feature id]</c> and <c>redgreen amend &lt;test&gt; --reason "…"</c> — the
/// red/green protocol, in cycles: <c>start</c> records the test inventory once (needs the tasks approval); each <c>red</c> accepts the
/// new tests of a cycle when they all fail for the right reason, which locks the test projects (<see cref="RedStep"/>); <c>green</c>
/// closes the cycle when they pass with nothing deleted, skipped, made explicit or broken (<see cref="GreenStep"/>). <c>amend</c>
/// (human-only) lets one test of the open cycle change until a red re-checks it (<see cref="AmendStep"/>); <c>reset</c> (human-only)
/// discards the evidence. A step whose tools cannot build, list or run the tests ends ERROR (exit 3), not FAIL. Runtime evidence and a guardrail,
/// not proof.
/// </summary>
internal sealed class RedGreenCommand(CliContext context) : ICommand
{
    private const string ResetVerb = "reset";
    private const string AmendVerb = "amend";
    private const string RedVerb = "red";
    private const string VerboseFlag = "--verbose";

    public async Task<ExitCode> RunAsync(CommandArguments arguments, CancellationToken cancellationToken)
    {
        (string verb, string[] args) = arguments.Verb();
        string? test = null;
        if (verb == AmendVerb)
        {
            if (args is not [string named, .. string[] rest] || named.StartsWith('-'))
            {
                await context.Error.WriteLineAsync($"lockflight redgreen amend: name the test to amend: {AmendStep.Usage}");
                return ExitCode.Config;
            }

            (test, args) = (named, rest);
        }

        if (await CommandOptions.ParseOrReportAsync($"redgreen {verb}", args, arguments.Command.ValueOptions, arguments.Command.FlagOptions, context.Error, cancellationToken)
            is not CommandOptions options)
        {
            return ExitCode.Config;
        }

        string? reason = options.Value("--reason")?.Trim();
        if (verb == AmendVerb && reason is not { Length: > 0 })
        {
            await context.Error.WriteLineAsync($"lockflight redgreen amend: --reason is required: say why the test must change ({AmendStep.Usage})");
            return ExitCode.Config;
        }

        if (await ManifestRequirement.LoadAsync(context, context.WorkingDirectory, cancellationToken) is not Manifest manifest)
        {
            return ExitCode.Config;
        }

        FeatureLookup lookup = FeatureLocator.Resolve(manifest.Root, options.Value("--feature"));
        if (lookup.Feature is not Feature feature)
        {
            await context.Error.WriteLineAsync($"lockflight: {lookup.Error}");
            return ExitCode.Config;
        }

        TestWorkspace workspace = TestWorkspaces.For(manifest, feature, context.Time);
        RedGreenSession session = new(context, manifest, feature, workspace);
        RedGreenRecord? record;
        try
        {
            record = RedGreenRecord.Load(session.RecordPath);
        }
        catch (Exception exception) when (verb == ResetVerb && exception is IOException or JsonException or FormatException or InvalidOperationException)
        {
            // An unreadable record is exactly what a person may need to discard.
            record = null;
        }

        return verb switch
        {
            "start" => await StartAsync(session, record, cancellationToken),
            RedVerb => await new RedStep(session, options.Flag(VerboseFlag)).RunAsync(record, cancellationToken),
            "green" => await new GreenStep(session).RunAsync(record, cancellationToken),
            "status" => await StatusAsync(feature, record),
            AmendVerb => await new AmendStep(session).RunAsync(record, test!, reason!, cancellationToken),
            ResetVerb => await ResetAsync(session, record, cancellationToken),
            _ => await UnknownAsync(verb),
        };
    }

    /// <summary>
    /// Deletes the feature's red/green evidence and the refs that keep its cycles' red-time snapshots, and ledgers it, whichever way the
    /// person reset it; says which refs it deleted, for the person's confirmation.
    /// </summary>
    public static async Task<string> DiscardAsync(RedGreenSession session, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> references = SnapshotRefs(session.Feature);
        File.Delete(session.RecordPath);
        IReadOnlyList<string> deleted = await RedSnapshots.DeleteAsync(
            session.Manifest.Root, references, TimeSpan.FromSeconds(session.Manifest.Gates.TimeoutSeconds), cancellationToken);
        await session.RecordAsync(LedgerSignatures.RedGreenReset, session.Feature.Id, cancellationToken);
        return deleted.Count switch
        {
            0 => string.Empty,
            1 => $" Deleted the red-time snapshot ref {deleted[0]}.",
            _ => $" Deleted the red-time snapshot refs {string.Join(", ", deleted)}.",
        };
    }

    /// <summary>The snapshot refs the feature's record names; none when there is no record or it cannot be read.</summary>
    public static IReadOnlyList<string> SnapshotRefs(Feature feature)
    {
        try
        {
            return RedGreenRecord.Load(RedGreenRecord.PathFor(feature)) is RedGreenRecord record ? RedSnapshots.RefsOf(feature.Id, record) : [];
        }
        catch (Exception exception) when (exception is IOException or JsonException or FormatException or InvalidOperationException)
        {
            return [];
        }
    }

    private async Task<ExitCode> StartAsync(RedGreenSession session, RedGreenRecord? record, CancellationToken cancellationToken)
    {
        if (record is not null)
        {
            return await session.FailAsync(
                $"the baseline was taken at {record.BaselineAt:u} (commit {RedGreenSession.Short(record.BaselineCommit)}); it is never retaken by the agent. Only a person can discard it: {HumanOnlyCommands.AskTheUser("redgreen reset")}.",
                cancellationToken);
        }

        ApprovalCheck tasks = new ApprovalStore(session.Feature).Check(ApprovalPhase.Tasks);
        if (tasks.State != ApprovalState.Valid)
        {
            return await session.FailAsync(tasks.Describe(), cancellationToken);
        }

        TestWorkspace workspace = session.Workspace;
        if (await workspace.BuildAsync(cancellationToken) is string buildOutput)
        {
            return await session.FailAsync($"the build fails; start from a building tree.\n{RedGreenSession.Tail(buildOutput)}", cancellationToken);
        }

        if (workspace.DiscoveryProblem is string incomplete)
        {
            return await session.NotEarnedAsync($"no baseline: {incomplete}", cancellationToken);
        }

        if (workspace.Apps.Count == 0)
        {
            await context.Output.WriteLineAsync("NOT-EARNED no xUnit test project found; red/green needs xUnit v3, or xUnit v2 on VSTest (other test frameworks get the gates only)");
            return ExitCode.NotEarned;
        }

        if (await workspace.ListAsync(cancellationToken) is not IReadOnlyList<TestCase> tests)
        {
            return await session.BrokenAsync($"the test runner could not list the tests of {workspace.FailedApp}", cancellationToken);
        }

        RedGreenRecord started = new()
        {
            BaselineCommit = await Git.HeadAsync(session.Manifest.Root, cancellationToken) ?? Git.UnknownCommit,
            BaselineAt = context.Time.GetUtcNow(),
            BaselineTests = tests.Select(test => test.Key).ToHashSet(StringComparer.Ordinal),
            BaselineSkipped = tests.Where(test => test.SkipReason is not null).Select(test => test.Key).ToHashSet(StringComparer.Ordinal),
            BaselineExplicit = tests.Where(test => test.IsExplicit).Select(test => test.Key).ToHashSet(StringComparer.Ordinal),
            TestProjects = session.RelativeProjects(),
        };
        session.Save(started);
        await session.RecordAsync(LedgerSignatures.RedGreenStart, $"{session.Feature.Id}: baseline of {tests.Count} tests in {workspace.Apps.Count} test projects", cancellationToken);
        await context.Output.WriteLineAsync($"Baseline: {tests.Count} tests in {string.Join(", ", started.TestProjects)} (commit {RedGreenSession.Short(started.BaselineCommit)})");
        await WarnAboutFailingBaselineAsync(session, cancellationToken);
        await context.Output.WriteLineAsync($"Next: declare stubs, write the first story's tests, then run '{EngineCommand.Linked} redgreen red'.");
        return ExitCode.Pass;
    }

    /// <summary>
    /// Runs the suite once before any change: a test that already fails (often for environmental reasons) would otherwise block every
    /// green with "other tests fail". Only a person accepts such failures, and the baseline must reach the base branch first.
    /// </summary>
    private async Task WarnAboutFailingBaselineAsync(RedGreenSession session, CancellationToken cancellationToken)
    {
        if (await session.Workspace.RunAsync(null, cancellationToken) is not IReadOnlyList<ExecutedTest> results)
        {
            await context.Output.WriteLineAsync($"WARN the test runner could not run the tests of {session.Workspace.FailedApp}, so tests that already fail are not known yet.");
            return;
        }

        IReadOnlySet<string> knownFailing = KnownFailingTests.LoadAll(session.Manifest);
        string[] failing = [.. results.Where(result => result.Outcome == TestOutcome.Fail && !knownFailing.Contains(result.Name)).Select(result => result.Name).Distinct().Order(StringComparer.Ordinal)];
        if (failing.Length == 0)
        {
            return;
        }

        await context.Output.WriteLineAsync($"WARN {failing.Length} {(failing.Length == 1 ? "test fails" : "tests fail")} before any change: {string.Join(", ", failing.Take(RedGreenSession.MaxTestsListed))}");
        await context.Output.WriteLineAsync(
            "     Every green will be refused while they fail. If they fail for reasons outside this feature, " +
            HumanOnlyCommands.AskTheUser("baseline tests") + ", and to merge that baseline in its own pull request first: CI " +
            "takes baselines from the base branch, so one added in the feature's pull request is discarded.");
    }

    private async Task<ExitCode> StatusAsync(Feature feature, RedGreenRecord? record)
    {
        if (record is null)
        {
            await context.Output.WriteLineAsync($"{feature.Id}: not started");
            return ExitCode.Pass;
        }

        await context.Output.WriteLineAsync($"{feature.Id}: baseline {record.BaselineTests.Count} tests at {RedGreenSession.Short(record.BaselineCommit)}");
        foreach (RedGreenCycle cycle in record.Cycles)
        {
            string state = cycle.IsOpen ? "RED (test projects locked)" : $"green at {cycle.GreenAt:u}";
            string amended = cycle.OpenAmends.Any() ? $"; amended, writable until the next red: {string.Join(", ", cycle.OpenAmends.SelectMany(amend => amend.Files).Distinct())}" : string.Empty;
            await context.Output.WriteLineAsync($"cycle {cycle.Number}: {cycle.Tests.Count} tests, {state}{amended}");
        }

        foreach (string line in RedGreenAmends.Lines(record))
        {
            await context.Output.WriteLineAsync($"amended: {line}");
        }

        return ExitCode.Pass;
    }

    private async Task<ExitCode> ResetAsync(RedGreenSession session, RedGreenRecord? record, CancellationToken cancellationToken)
    {
        string feature = session.Feature.Id;
        if (record is null && !File.Exists(session.RecordPath))
        {
            await context.Output.WriteLineAsync($"{feature}: no red/green evidence to discard; nothing was reset.");
            return ExitCode.Pass;
        }

        string refs = await DiscardAsync(session, cancellationToken);
        await context.Output.WriteLineAsync($"{feature}: red/green evidence discarded; the test projects are unlocked for this feature.{refs}");
        return ExitCode.Pass;
    }

    private async Task<ExitCode> UnknownAsync(string verb)
    {
        await context.Error.WriteLineAsync($"lockflight: unknown redgreen step '{verb}'; use start, red, green, status, amend or reset");
        return ExitCode.Config;
    }
}
