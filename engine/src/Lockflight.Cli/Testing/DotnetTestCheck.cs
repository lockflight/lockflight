using System.Xml;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Gates.Parsers;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Testing;

/// <summary>
/// Runs one test through a plain <c>dotnet test</c> (<see cref="TestCommands.DotnetTest"/>, after the engine's build, so
/// <c>--no-build</c>) to see whether it passes there too. On VSTest the run's TRX says; on Microsoft.Testing.Platform its exit code
/// (0 passed, 2 a test failed). Anything else, a time-out or a run that executed no test, is inconclusive.
/// </summary>
internal static class DotnetTestCheck
{
    private const string ReportName = "check.trx";

    private const int MtpTestsFailed = 2;

    /// <param name="platform">The platform <c>dotnet test</c> uses for the project (<see cref="TestRunnerDetection"/>).</param>
    public static async Task<DotnetTestOutcome> RunAsync(TestCommand command, TestRunnerKind platform, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        if (platform == TestRunnerKind.MicrosoftTestingPlatform)
        {
            ProcessResult run = await ProcessRunner.RunAsync("dotnet", [.. command.Arguments, .. BuildServerSwitches.Properties], command.WorkingDirectory, timeLimit, cancellationToken);
            return run.TimedOut ? DotnetTestOutcome.Inconclusive : run.ExitCode switch
            {
                0 => DotnetTestOutcome.Passed,
                MtpTestsFailed => DotnetTestOutcome.Failed,
                _ => DotnetTestOutcome.Inconclusive,
            };
        }

        DirectoryInfo results = Directory.CreateTempSubdirectory("lockflight-check-");
        try
        {
            ProcessResult run = await ProcessRunner.RunAsync(
                "dotnet", [.. command.Arguments, .. BuildServerSwitches.Properties, "--logger", $"trx;LogFileName={ReportName}", "--results-directory", results.FullName], command.WorkingDirectory, timeLimit, cancellationToken);
            string[] reports = Directory.GetFiles(results.FullName, "*.trx", SearchOption.AllDirectories);
            if (run.TimedOut || reports.Length == 0)
            {
                return DotnetTestOutcome.Inconclusive;
            }

            TrxSummary summary = TrxReports.Read(reports);
            return summary.Executed == 0 || summary.Incomplete.Count > 0 ? DotnetTestOutcome.Inconclusive
                : summary.Failed > 0 || summary.AbnormalTests.Count > 0 ? DotnetTestOutcome.Failed
                : DotnetTestOutcome.Passed;
        }
        catch (XmlException)
        {
            return DotnetTestOutcome.Inconclusive;
        }
        finally
        {
            results.Delete(recursive: true);
        }
    }
}
