using System.Xml;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Testing;

/// <summary>
/// Runs tests of an xUnit v3 application selected by method and collects xUnit XML (each failure's exception type and stack):
/// through the in-process console runner (<c>-method … -result-xml …</c>, <c>-xml</c> before xUnit v3 4.0) or, for MTP-only apps, through xUnit's MTP options
/// (<c>--filter-method … --report-xunit-xml</c>). Method filters go in batches so the command line stays under Windows' limit.
/// </summary>
internal static class NativeTestRunner
{
    private const int MethodsPerBatch = 40;

    /// <summary>The in-process console runner's exit code when at least one test failed.</summary>
    private const int NativeTestsFailed = 1;

    /// <summary>MTP's exit code when at least one test failed.</summary>
    private const int MtpTestsFailed = 2;

    /// <summary>MTP's exit code when the filter selected no test.</summary>
    private const int MtpZeroTests = 8;

    /// <summary>
    /// Runs the given methods (all tests when <paramref name="methodFilters"/> is null); null when the runner produced no report. The
    /// first exit code that is not a test verdict is kept in the report, so a run that crashed half-way is not taken at its word.
    /// </summary>
    public static async Task<TestRunReport?> RunAsync(
        TestApp app, IReadOnlyList<string>? methodFilters, string workingDirectory, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        if (app.Mode == RunnerMode.VSTest)
        {
            return await VSTestRunner.RunAsync(app, methodFilters, workingDirectory, timeLimit, cancellationToken);
        }

        List<ExecutedTest> results = [];
        int? abnormalExitCode = null;
        IEnumerable<string[]> batches = methodFilters is null ? [[]] : methodFilters.Chunk(MethodsPerBatch);
        foreach (string[] batch in batches)
        {
            string report = Path.Combine(Path.GetTempPath(), $"lockflight-tests-{Guid.CreateVersion7()}.xml");
            try
            {
                List<string> arguments = app.Mode == RunnerMode.Mtp
                    ? [app.AssemblyPath, "--no-ansi", "--no-progress", "--report-xunit-xml", "--report-xunit-xml-filename", Path.GetFileName(report),
                       "--results-directory", Path.GetDirectoryName(report)!]
                    : [app.AssemblyPath, "-noLogo", "-noColor", InProcessRunner.ResultXmlSwitch(InProcessRunner.VersionOf(app.AssemblyPath)), report];
                foreach (string method in batch)
                {
                    arguments.AddRange([app.Mode == RunnerMode.Mtp ? "--filter-method" : "-method", method]);
                }

                ProcessResult run = await ProcessRunner.RunAsync("dotnet", arguments, workingDirectory, timeLimit, cancellationToken);
                if (!File.Exists(report))
                {
                    return null;
                }

                abnormalExitCode ??= IsTestVerdict(app.Mode, run) ? null : run.ExitCode;
                results.AddRange(Parse(app.AssemblyName, await File.ReadAllTextAsync(report, cancellationToken)));
            }
            finally
            {
                File.Delete(report);
            }
        }

        return new TestRunReport(results, abnormalExitCode);
    }

    /// <summary>The runner finished and its exit code says only whether tests passed.</summary>
    private static bool IsTestVerdict(RunnerMode mode, ProcessResult run) =>
        !run.TimedOut && (run.ExitCode == 0 || (mode == RunnerMode.Mtp ? run.ExitCode is MtpTestsFailed or MtpZeroTests : run.ExitCode == NativeTestsFailed));

    public static IReadOnlyList<ExecutedTest> Parse(string assembly, string xml)
    {
        XmlDocument document = new() { XmlResolver = null };
        document.LoadXml(xml);
        List<ExecutedTest> results = [];
        foreach (XmlElement test in document.GetElementsByTagName("test").OfType<XmlElement>())
        {
            XmlElement? failure = test["failure"];
            TestOutcome outcome = test.GetAttribute("result") switch
            {
                "Pass" => TestOutcome.Pass,
                "Fail" => TestOutcome.Fail,
                "Skip" => TestOutcome.Skip,
                _ => TestOutcome.NotRun,
            };
            results.Add(new ExecutedTest(
                $"{assembly}:{test.GetAttribute("type")}.{test.GetAttribute("method")}",
                test.GetAttribute("name"),
                outcome,
                failure?.GetAttribute("exception-type") is { Length: > 0 } type ? type : null,
                failure is null ? null : TopFrame(failure["stack-trace"]?.InnerText).Method,
                failure is null ? null : TopFrame(failure["stack-trace"]?.InnerText).File));
        }

        return results;
    }

    /// <summary><c>at Type.Method(args) in /path/File.cs:line 22</c> → method and file (file null without symbols).</summary>
    public static (string? Method, string? File) TopFrame(string? stackTrace)
    {
        string? first = stackTrace?.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.StartsWith("at ", StringComparison.Ordinal));
        if (first is null)
        {
            return (null, null);
        }

        int location = first.IndexOf(" in ", StringComparison.Ordinal);
        if (location < 0)
        {
            return (first[3..].Trim(), null);
        }

        string file = first[(location + 4)..];
        int line = file.LastIndexOf(":line ", StringComparison.Ordinal);
        return (first[3..location].Trim(), (line > 0 ? file[..line] : file).Trim());
    }
}
