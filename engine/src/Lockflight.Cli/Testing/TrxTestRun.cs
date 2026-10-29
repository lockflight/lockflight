using Lockflight.Cli.Gates;
using Lockflight.Cli.Gates.Parsers;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Testing;

/// <summary>
/// Runs a test assembly once per batch of selection arguments and reads each run's TRX: through VSTest (<c>dotnet test &lt;dll&gt;
/// --logger trx</c>) or, for a Microsoft.Testing.Platform application, the application itself (<c>dotnet &lt;dll&gt; --report-trx</c>).
/// The first exit code that is not a test verdict, or a TRX that says the run did not complete, is kept in the report, so a run that
/// crashed half-way is not taken at its word. The batches share the time limit; null when it runs out before the last batch or a run
/// wrote no report.
/// </summary>
internal static class TrxTestRun
{
    private const string ReportName = "results.trx";

    /// <summary>VSTest's exit code when a test failed; any other non-zero exit is abnormal.</summary>
    private const int VSTestTestsFailed = 1;

    /// <summary>Microsoft.Testing.Platform's exit code when a test failed.</summary>
    private const int MtpTestsFailed = 2;

    /// <summary>Microsoft.Testing.Platform's exit code when the selection matched no test; its TRX then says "Failed" with no result.</summary>
    private const int MtpZeroTests = 8;

    public static async Task<TestRunReport?> RunAsync(
        TestApp app,
        TestRunnerKind platform,
        IEnumerable<IReadOnlyList<string>> batches,
        Func<string, string?> exceptionTypeOf,
        string workingDirectory,
        TimeSpan timeLimit,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        List<ExecutedTest> results = [];
        int? abnormalExitCode = null;
        TimeBudget budget = new(timeLimit, time);
        foreach (IReadOnlyList<string> batch in batches)
        {
            if (budget.IsSpent)
            {
                return null;
            }

            DirectoryInfo directory = Directory.CreateTempSubdirectory("lockflight-trx-");
            try
            {
                List<string> arguments = platform == TestRunnerKind.MicrosoftTestingPlatform
                    ? [app.AssemblyPath, "--no-ansi", "--report-trx", "--report-trx-filename", ReportName, "--results-directory", directory.FullName]
                    : ["test", app.AssemblyPath, "--logger", $"trx;LogFileName={ReportName}", "--results-directory", directory.FullName, "--nologo"];
                arguments.AddRange(batch);
                ProcessResult run = await ProcessRunner.RunAsync("dotnet", arguments, workingDirectory, budget.Left, cancellationToken);
                string report = Path.Combine(directory.FullName, ReportName);
                if (!File.Exists(report))
                {
                    return null;
                }

                abnormalExitCode ??= Completed(platform, run, report) ? null : run.ExitCode;
                results.AddRange(TrxTestResults.Parse(app.AssemblyName, await File.ReadAllTextAsync(report, cancellationToken), exceptionTypeOf));
            }
            finally
            {
                directory.Delete(recursive: true);
            }
        }

        return new TestRunReport(results, abnormalExitCode);
    }

    /// <summary>The run finished, its exit code is a test verdict and its TRX does not say the run broke off (a crashed test host also exits "failed").</summary>
    private static bool Completed(TestRunnerKind platform, ProcessResult run, string report)
    {
        if (run.TimedOut)
        {
            return false;
        }

        if (platform == TestRunnerKind.MicrosoftTestingPlatform)
        {
            return run.ExitCode == MtpZeroTests || (run.ExitCode is (0 or MtpTestsFailed) && TrxReports.Read([report]).Incomplete.Count == 0);
        }

        return run.ExitCode is (0 or VSTestTestsFailed) && TrxReports.Read([report]).Incomplete.Count == 0;
    }
}
