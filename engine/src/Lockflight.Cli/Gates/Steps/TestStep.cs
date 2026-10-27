using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using Lockflight.Cli.Gates.Baselines;
using Lockflight.Cli.Gates.Parsers;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Gates.Steps;

/// <summary>
/// Tests on Microsoft.Testing.Platform or VSTest, whichever <c>dotnet test</c> uses in the target; failures listed in the
/// known-failing baseline do not fail the gate — but only when every failure the runner counted was matched to a test name, so a
/// failure the output does not name can never hide behind the baseline.
/// </summary>
internal static partial class TestStep
{
    public const string Name = "test";

    /// <summary>Where a VSTest run writes its TRX and coverage files: git-ignored local state, emptied before each run.</summary>
    public const string ResultsRelativePath = ".lockflight/local/test-results";

    public static Task<TestStepOutcome> RunAsync(GateRunContext run, CancellationToken cancellationToken) =>
        TestRunnerDetection.For(run.Target) == TestRunnerKind.MicrosoftTestingPlatform
            ? RunOnTestingPlatformAsync(run, cancellationToken)
            : RunOnVSTestAsync(run, cancellationToken);

    private static async Task<TestStepOutcome> RunOnVSTestAsync(GateRunContext run, CancellationToken cancellationToken)
    {
        // Per unit: units of a workspace run in parallel and must not empty each other's results.
        string results = Path.Combine(run.ProjectRoot, ResultsRelativePath, run.UnitPath ?? string.Empty);
        if (Directory.Exists(results))
        {
            Directory.Delete(results, recursive: true);
        }

        List<string> arguments = ["test", .. run.BuildFileArguments, "--no-build", "--logger", "trx;LogFilePrefix=lockflight", "--results-directory", results];
        if (run.Settings.TestFramework is string framework)
        {
            arguments.AddRange(["--framework", framework]);
        }

        if (run.Profile == GateProfile.PullRequest)
        {
            arguments.AddRange(CoverageStep.TestArguments(run.Settings.Coverage.Collector, TestRunnerKind.VSTest));
        }
        else if (run.StopFilter is string filter)
        {
            arguments.AddRange(["--filter", filter]);
        }

        ProcessResult testRun = await ProcessRunner.RunAsync("dotnet", arguments, run.Target, run.TimeLeft(), cancellationToken);
        if (StepHealth.Unhealthy(Name, testRun, run) is GateStep unhealthy)
        {
            return new TestStepOutcome(unhealthy, null, []) { ResultsDirectory = results };
        }

        string[] reports = Directory.Exists(results) ? Directory.GetFiles(results, "*.trx", SearchOption.AllDirectories) : [];
        TrxSummary summary;
        try
        {
            summary = TrxReports.Read(reports);
        }
        catch (Exception exception) when (exception is XmlException or IOException)
        {
            return new TestStepOutcome(new GateStep(Name, testRun.ExitCode, Verdict.Error, testRun.DurationMs, $"a TRX report could not be read: {exception.Message}"), null, []) { ResultsDirectory = results };
        }

        GateStep step = JudgeVSTest(testRun, reports.Length, summary, () => new KnownFailingTests(run.ProjectRoot, run.UnitPath).Load(), AppliedStopFilter(run));
        return new TestStepOutcome(step, reports.Length == 0 ? null : summary.Total, summary.FailedTests) { ResultsDirectory = results };
    }

    /// <summary>
    /// VSTest's exit code says only "something failed" (1): the TRX counters decide. No executed test is NOT-EARNED (a filter or a
    /// solution without tests exits 0); a non-zero exit without a failed test means a project or test host broke, which is ERROR.
    /// A run that did not complete (a crashed test host drops its results) is ERROR and a result that timed out, was aborted or
    /// errored FAIL, both before the known-failing baseline: a baseline covers failures it can see, never results that are missing.
    /// <paramref name="stopFilter"/> is the filter the run applied (null for none), named when nothing ran.
    /// </summary>
    public static GateStep JudgeVSTest(ProcessResult testRun, int reportCount, TrxSummary summary, Func<IReadOnlySet<string>> knownFailures, string? stopFilter)
    {
        if (reportCount == 0)
        {
            return testRun.ExitCode == 0
                ? new GateStep(Name, testRun.ExitCode, Verdict.NotEarned, testRun.DurationMs, NoTestsRan(stopFilter, "no test project found: dotnet test wrote no TRX report"))
                : new GateStep(Name, testRun.ExitCode, Verdict.Error, testRun.DurationMs, $"dotnet test exited {testRun.ExitCode} without writing a TRX report; run 'dotnet test' to see why");
        }

        if (summary.Incomplete.Count > 0)
        {
            return new GateStep(Name, testRun.ExitCode, Verdict.Error, testRun.DurationMs,
                $"a test run did not complete, so its results are missing ({string.Join("; ", summary.Incomplete)}); run 'dotnet test' to see why");
        }

        if (summary.AbnormalTests.Count > 0)
        {
            return new GateStep(Name, testRun.ExitCode, Verdict.Fail, testRun.DurationMs,
                FailedTests.Describe(summary.AbnormalTests, "did not complete (timeout, aborted or error)"));
        }

        if (summary.Failed > 0)
        {
            return JudgeAgainstBaseline(testRun, summary.Total, [.. summary.FailedTests.Distinct(StringComparer.Ordinal)], summary.Failed, summary.FailedTests.Count, knownFailures());
        }

        if (testRun.ExitCode != 0)
        {
            return new GateStep(Name, testRun.ExitCode, Verdict.Error, testRun.DurationMs, $"dotnet test exited {testRun.ExitCode} but no test failed: a project or a test host did not run; run 'dotnet test' to see why");
        }

        return summary.Executed == 0
            ? new GateStep(Name, testRun.ExitCode, Verdict.NotEarned, testRun.DurationMs, NoTestsRan(stopFilter, summary.Total == 0
                ? "the TRX reports count no test"
                : $"the TRX reports count {summary.Total} tests and none executed: all skipped or not run"))
            : new GateStep(Name, testRun.ExitCode, Verdict.Pass, testRun.DurationMs, $"total {summary.Total}");
    }

    private static async Task<TestStepOutcome> RunOnTestingPlatformAsync(GateRunContext run, CancellationToken cancellationToken)
    {
        List<string> arguments = ["test", .. run.BuildFileArguments, "--no-build"];
        if (run.Profile != GateProfile.PullRequest && run.StopFilter is string filter)
        {
            if (TraitFilter(filter) is not string[] traitArguments)
            {
                return new TestStepOutcome(new GateStep(Name, 0, Verdict.Error, 0,
                    $"test.stopFilter '{filter}' is not supported on Microsoft.Testing.Platform; use one trait condition such as Category!=Integration"), null, []);
            }

            arguments.AddRange(traitArguments);
        }

        if (run.Settings.TestFramework is string framework)
        {
            arguments.AddRange(["--framework", framework]);
        }

        IReadOnlyList<string> coverageArguments = run.Profile == GateProfile.PullRequest ? CoverageStep.TestArguments(run.Settings.Coverage.Collector, TestRunnerKind.MicrosoftTestingPlatform) : [];
        arguments.AddRange(coverageArguments);

        ProcessResult coloured = await ProcessRunner.RunAsync("dotnet", arguments, run.Target, run.TimeLeft(), cancellationToken);
        ProcessResult testRun = coloured with { Output = AnsiEscapes.Strip(coloured.Output), Error = AnsiEscapes.Strip(coloured.Error) };
        int? testsTotal = SumOf(TestsTotalPattern(), testRun.Output);
        IReadOnlyList<string> failedTests = FailedTests.Find(testRun.Output);
        if (StepHealth.Unhealthy(Name, testRun, run) is GateStep unhealthy)
        {
            return new TestStepOutcome(unhealthy, testsTotal, failedTests);
        }

        if (RejectedCommandLine(testRun))
        {
            string explanation = coverageArguments.Count > 0
                ? CoverageStep.MissingExtension(run.Settings.Coverage.Collector, TestRunnerKind.MicrosoftTestingPlatform)
                : "a test application rejected its command line (exit 5, invalid command line); check gates.test.framework in lockflight.yaml and run 'dotnet test' to see the message";
            return new TestStepOutcome(new GateStep(Name, testRun.ExitCode, Verdict.Error, testRun.DurationMs, explanation), testsTotal, failedTests);
        }

        GateStep step = JudgeTestingPlatform(testRun, AppliedStopFilter(run));
        if (step.Verdict == Verdict.Fail && failedTests.Count > 0)
        {
            IReadOnlySet<string> known = new KnownFailingTests(run.ProjectRoot, run.UnitPath).Load();
            return new TestStepOutcome(JudgeAgainstBaseline(testRun, testsTotal, failedTests, known), testsTotal, failedTests);
        }

        return new TestStepOutcome(step, testsTotal, failedTests);
    }

    /// <summary>
    /// Microsoft.Testing.Platform's exit code read with its summary. A success proves something only when the summary counts a test
    /// that executed (succeeded or failed): a run whose every test was skipped exits 0 too ("Zero tests ran"), and a summary without
    /// the counts says nothing, so both are NOT-EARNED. Exit 8 (zero tests) from a run in which other test applications executed
    /// tests is judged by those tests: a filter that leaves one project with nothing to run does not void the others.
    /// <paramref name="stopFilter"/> is the filter the run applied (null for none), named when nothing ran.
    /// </summary>
    public static GateStep JudgeTestingPlatform(ProcessResult testRun, string? stopFilter)
    {
        int? testsTotal = SumOf(TestsTotalPattern(), testRun.Output);
        Verdict verdict = Judge(testRun.ExitCode, testsTotal);
        if (verdict == Verdict.NotEarned || (verdict == Verdict.Pass && !ExecutedAny(testRun.Output)))
        {
            return new GateStep(Name, testRun.ExitCode, Verdict.NotEarned, testRun.DurationMs, NoTestsRan(stopFilter, NothingExecutedReason(testRun.Output, testsTotal)));
        }

        if (verdict == Verdict.Error)
        {
            return new GateStep(Name, testRun.ExitCode, verdict, testRun.DurationMs, $"dotnet test exited {testRun.ExitCode}, which is not a test verdict");
        }

        string[] idle = [.. ZeroTestsModulePattern().Matches(testRun.Output).Select(match => Path.GetFileName(match.Groups["module"].Value.Trim())).Distinct(StringComparer.Ordinal)];
        string detail = verdict == Verdict.Pass && idle.Length > 0 ? $"total {testsTotal}; ran no test: {string.Join(", ", idle)}" : $"total {testsTotal}";
        return new GateStep(Name, testRun.ExitCode, verdict, testRun.DurationMs, detail);
    }

    /// <summary>The summary counts a test that passed or failed; a summary without the succeeded figure proves no test executed.</summary>
    private static bool ExecutedAny(string output) =>
        SumOf(SucceededCountPattern(), output) is int succeeded && succeeded + (SumOf(FailedCountPattern(), output) ?? 0) > 0;

    private static string NothingExecutedReason(string output, int? testsTotal) => testsTotal switch
    {
        null => "no test project found, or the runner's summary was missing",
        0 => "the runner's summary counts no test",
        _ when SumOf(SucceededCountPattern(), output) is null => $"the runner's summary counts {testsTotal} tests but not how many executed",
        _ => $"the runner's summary counts {testsTotal} tests and none executed: all skipped or not run",
    };

    /// <summary>
    /// Why a run executed no test. A stop filter that narrowed the run comes first: one that matches nothing is the likeliest cause,
    /// and a stop gate that runs no test proves nothing.
    /// </summary>
    private static string NoTestsRan(string? stopFilter, string reason) => stopFilter is null
        ? $"no tests ran ({reason})"
        : $"no tests ran: test.stopFilter '{stopFilter}' matched no test that runs ({reason}); check the filter in lockflight.yaml";

    /// <summary>The stop filter when this run applied it: the pr profile runs every test.</summary>
    private static string? AppliedStopFilter(GateRunContext run) => run.Profile == GateProfile.PullRequest ? null : run.StopFilter;

    /// <summary>
    /// A VSTest-style trait condition (<c>Category!=Integration</c>, <c>Category=Fast</c>) as xUnit v3's own options; null for
    /// anything else, which Microsoft.Testing.Platform cannot express the same way.
    /// </summary>
    public static string[]? TraitFilter(string filter)
    {
        Match match = TraitCondition().Match(filter);
        return !match.Success ? null : match.Groups["not"].Success
            ? ["--filter-not-trait", $"{match.Groups["name"].Value}={match.Groups["value"].Value}"]
            : ["--filter-trait", $"{match.Groups["name"].Value}={match.Groups["value"].Value}"];
    }

    /// <summary>
    /// PASS when every failure is a known one: each named failure is in the baseline and the named failures account for every failure
    /// in the runner's summary. A failure without a name (a crashed test host, output the parser does not recognise) fails the step.
    /// </summary>
    public static GateStep JudgeAgainstBaseline(ProcessResult testRun, int? testsTotal, IReadOnlyList<string> failedTests, IReadOnlySet<string> known) =>
        JudgeAgainstBaseline(testRun, testsTotal, failedTests, SumOf(FailedCountPattern(), testRun.Output), FailedTests.CountFailures(testRun.Output), known);

    /// <summary>The same judgement with the runner's count of failures and the number of named failure results given directly.</summary>
    public static GateStep JudgeAgainstBaseline(ProcessResult testRun, int? testsTotal, IReadOnlyList<string> failedTests, int? countedFailures, int named, IReadOnlySet<string> known)
    {
        List<string> unexpected = [.. failedTests.Where(name => !known.Contains(name))];
        if (unexpected.Count > 0)
        {
            return new GateStep(Name, testRun.ExitCode, Verdict.Fail, testRun.DurationMs, FailedTests.Describe(unexpected));
        }

        if (countedFailures is not int counted)
        {
            return new GateStep(Name, testRun.ExitCode, Verdict.Fail, testRun.DurationMs,
                $"{named} failures are known (baseline), but the runner's summary of failures is missing, so further failures could not be ruled out");
        }

        return counted == named
            ? new GateStep(Name, testRun.ExitCode, Verdict.Pass, testRun.DurationMs, $"total {testsTotal}; {failedTests.Count} known failures (baseline)")
            : new GateStep(Name, testRun.ExitCode, Verdict.Fail, testRun.DurationMs,
                $"{Math.Abs(counted - named)} failures could not be attributed: the runner counted {counted} failed and named {named}, all known (baseline); run 'dotnet test' to see them");
    }

    private static Verdict Judge(int exitCode, int? testsTotal) => (TestPlatformExitCode)exitCode switch
    {
        // Success with no test count (no test project found, or output we cannot read) proves nothing: NOT-EARNED, never PASS.
        // Exit 8 says some test application ran no test; whether any other one executed a test is read from the summary.
        TestPlatformExitCode.Success or TestPlatformExitCode.ZeroTests when testsTotal is null or 0 => Verdict.NotEarned,
        TestPlatformExitCode.Success or TestPlatformExitCode.ZeroTests => Verdict.Pass,
        TestPlatformExitCode.TestsFailed => Verdict.Fail,
        _ => Verdict.Error,
    };

    /// <summary>
    /// The whole run's exit code is 5, or the SDK aggregated a module's "Exit code: 5" into a generic failure: a test application refused
    /// an option. Zero tests ran, so this says nothing about the code.
    /// </summary>
    private static bool RejectedCommandLine(ProcessResult testRun) =>
        testRun.ExitCode == (int)TestPlatformExitCode.InvalidCommandLine
        || (testRun.ExitCode != 0 && ModuleExitedWithInvalidCommandLine().IsMatch(testRun.Combined));

    /// <summary>The sum of a summary figure over every test module's summary; null when no summary has it.</summary>
    private static int? SumOf(Regex figure, string output)
    {
        MatchCollection matches = figure.Matches(output);
        return matches.Count == 0 ? null : matches.Sum(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
    }

    [GeneratedRegex(@"^\s*(?<name>[A-Za-z_][A-Za-z0-9_.]*)\s*(?<not>!)?=\s*(?<value>[^&|()!=]+?)\s*$")]
    private static partial Regex TraitCondition();

    [GeneratedRegex(@"^\s*Exit code: 5\s*$", RegexOptions.Multiline)]
    private static partial Regex ModuleExitedWithInvalidCommandLine();

    [GeneratedRegex(@"total:\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex TestsTotalPattern();

    [GeneratedRegex(@"^\s*failed:\s*(\d+)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex FailedCountPattern();

    [GeneratedRegex(@"^\s*succeeded:\s*(\d+)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SucceededCountPattern();

    [GeneratedRegex(@"^\s*(?<module>\S.*?\.dll) \([^)]*\) Zero tests ran\b", RegexOptions.Multiline)]
    private static partial Regex ZeroTestsModulePattern();
}
