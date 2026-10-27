using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Canaries;

/// <summary>
/// <c>lockflight doctor --canaries[=full]</c>: plants a violation in front of every control and reports whether it was caught. All of
/// it happens in temporary scratch projects, never in the project the command is run from. Exits non-zero when any canary is missed
/// or could not judge, so a safeguard that silently stopped working turns this red.
/// </summary>
internal static class CanarySuite
{
    /// <summary>A control's answer is cut to this many characters: enough to see which rule spoke.</summary>
    private const int MaxDetailLength = 140;

    public static async Task<IReadOnlyList<CanaryReport>> RunAsync(CanaryMode mode, TimeProvider time, CancellationToken cancellationToken)
    {
        List<CanaryReport> reports = [];
        using (CanaryScratch scratch = new(time))
        {
            reports.AddRange(await GuardCanaries.RunAsync(scratch, cancellationToken));
            reports.AddRange(await HumanOnlyCanaries.RunAsync(scratch, cancellationToken));
            reports.AddRange(TestGateCanaries.Run(scratch));
            reports.Add(RedCanaries.Run());
        }

        if (mode == CanaryMode.Full)
        {
            reports.AddRange(await FullCanaries.RunAsync(time, cancellationToken));
        }

        return reports;
    }

    public static async Task<ExitCode> ReportAsync(IReadOnlyList<CanaryReport> reports, CanaryMode mode, TextWriter output)
    {
        foreach (CanaryReport report in reports)
        {
            string detail = report.Detail.Length <= MaxDetailLength ? report.Detail : report.Detail[..MaxDetailLength] + "…";
            await output.WriteLineAsync($"{Label(report.Result),-7} {report.Name}: {detail}");
        }

        int caught = reports.Count(report => report.Result == CanaryResult.Caught);
        await output.WriteLineAsync($"{caught} of {reports.Count} canaries caught.");
        if (mode == CanaryMode.Fast)
        {
            await output.WriteLineAsync("The canaries that build and run a generated project run with --canaries=full.");
        }

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
