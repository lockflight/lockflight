namespace Lockflight.Cli.Processes;

/// <summary>
/// For callers that parse a tool's output (a test listing, MSBuild's properties, a package report): output that a left-over process
/// held open may be cut anywhere, so it is never parsed. The tool runs once more; when its output is held again, the caller reports
/// <see cref="Problem"/> as an error instead of a verdict.
/// </summary>
internal static class CompleteOutput
{
    /// <summary>Runs the tool, and once more when its output was held; the second result may still be held.</summary>
    public static async Task<ProcessResult> RunAsync(Func<Task<ProcessResult>> run)
    {
        ProcessResult result = await run();
        return result.OutputHeld && !result.TimedOut ? await run() : result;
    }

    /// <summary>Why a held output was not read, for the error the caller reports.</summary>
    public static string Problem(string command) =>
        $"{command} exited, but a process it started kept its output open (twice), so the output may be cut and was not read; "
        + "stop that process in the build or the test's teardown, then re-run";
}
