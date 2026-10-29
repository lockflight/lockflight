using System.Diagnostics;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Canaries;

/// <summary>
/// <c>lockflight doctor --self-test</c>: a few canaries that show this engine build works on this machine. The guard blocks an
/// Edit of a protected file, a human-only command refuses inside a Claude Code session, a linked engine without its hash record is
/// not trusted, the test gate does not PASS a failing test, and red rejects a test that fails by <c>Assert.Fail</c>. Each is planted
/// in a temporary scratch project, never in the project the command is run from, and nothing is built. Exits non-zero when any
/// canary is missed or could not judge. The full canary suite, one in front of every registered guard rule and gate step, is part
/// of the engine's tests and runs in CI on Linux, Windows and macOS.
/// </summary>
internal static class SelfTestSuite
{
    /// <summary>A control's answer is cut to this many characters: enough to see which rule spoke.</summary>
    private const int MaxDetailLength = 140;

    public static async Task<IReadOnlyList<CanaryReport>> RunAsync(TimeProvider time, CancellationToken cancellationToken)
    {
        using CanaryScratch scratch = new(time);
        return
        [
            await GuardCanary.RunAsync(scratch, cancellationToken),
            await HumanOnlyCanary.RunAsync(scratch, cancellationToken),
            LinkedEngineCanary.Run(scratch),
            TestGateCanary.Run(scratch),
            RedCanary.Run(),
        ];
    }

    /// <summary>Runs the canaries and prints each, the count and how long they took.</summary>
    public static async Task<ExitCode> RunAndReportAsync(TimeProvider time, TextWriter output, CancellationToken cancellationToken)
    {
        long started = Stopwatch.GetTimestamp();
        IReadOnlyList<CanaryReport> reports = await RunAsync(time, cancellationToken);
        return await ReportAsync(reports, Stopwatch.GetElapsedTime(started), output);
    }

    public static async Task<ExitCode> ReportAsync(IReadOnlyList<CanaryReport> reports, TimeSpan elapsed, TextWriter output)
    {
        foreach (CanaryReport report in reports)
        {
            string detail = report.Detail.Length <= MaxDetailLength ? report.Detail : report.Detail[..MaxDetailLength] + "…";
            await output.WriteLineAsync($"{Label(report.Result),-7} {report.Name}: {detail}");
        }

        int caught = reports.Count(report => report.Result == CanaryResult.Caught);
        await output.WriteLineAsync($"{caught} of {reports.Count} self-test canaries caught in {elapsed.TotalMilliseconds:0} ms.");
        return ExitCodeFor(reports);
    }

    /// <summary>PASS only when every canary was caught: a missed one is a safeguard that does not work, a broken one proves nothing.</summary>
    public static ExitCode ExitCodeFor(IReadOnlyList<CanaryReport> reports) =>
        reports.Count > 0 && reports.All(report => report.Result == CanaryResult.Caught) ? ExitCode.Pass : ExitCode.Fail;

    private static string Label(CanaryResult result) => result switch
    {
        CanaryResult.Caught => "caught",
        CanaryResult.Missed => "MISSED",
        _ => "BROKEN",
    };
}
