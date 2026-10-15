using System.Text.RegularExpressions;
using System.Xml;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Testing;

/// <summary>
/// Runs tests of a VSTest (xUnit v2) test assembly selected by method — <c>dotnet test &lt;dll&gt; --filter
/// "FullyQualifiedName=A|FullyQualifiedName=B"</c> in batches — and reads the TRX: one result per executed case (theory rows
/// included), keyed by the test definition's class and method, with each failure's exception type and first stack frame. An
/// xUnit v2 assertion prints no exception type but a message starting <c>Assert.Equal() Failure</c>; it is reported as
/// <c>Xunit.Sdk.XunitException</c>, the base of xUnit's assertion exceptions. A message that names neither an exception type nor
/// an assertion (a fixture or constructor failure) has no type, so the red classifier judges it by its stack, never as an assertion.
/// </summary>
internal static partial class VSTestRunner
{
    public const string AssertionException = "Xunit.Sdk.XunitException";

    private const int MethodsPerBatch = 40;

    /// <summary>VSTest's exit code when a test failed; any other non-zero exit is abnormal.</summary>
    private const int TestsFailed = 1;

    public static async Task<TestRunReport?> RunAsync(
        TestApp app, IReadOnlyList<string>? methodFilters, string workingDirectory, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        List<ExecutedTest> results = [];
        int? abnormalExitCode = null;
        IEnumerable<string[]> batches = methodFilters is null ? [[]] : methodFilters.Chunk(MethodsPerBatch);
        System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();
        foreach (string[] batch in batches)
        {
            // The batches share the time limit: a long list of methods cannot multiply it.
            TimeSpan left = timeLimit == Timeout.InfiniteTimeSpan ? timeLimit : timeLimit - elapsed.Elapsed;
            if (left <= TimeSpan.Zero)
            {
                return null;
            }

            DirectoryInfo directory = Directory.CreateTempSubdirectory("lockflight-vstest-");
            try
            {
                List<string> arguments = ["test", app.AssemblyPath, "--logger", "trx;LogFileName=results.trx", "--results-directory", directory.FullName, "--nologo"];
                if (batch.Length > 0)
                {
                    arguments.AddRange(["--filter", string.Join('|', batch.Select(method => $"FullyQualifiedName={Escape(method)}"))]);
                }

                ProcessResult run = await ProcessRunner.RunAsync("dotnet", arguments, workingDirectory, left, cancellationToken);
                string report = Path.Combine(directory.FullName, "results.trx");
                if (!File.Exists(report))
                {
                    return null;
                }

                abnormalExitCode ??= !run.TimedOut && run.ExitCode is 0 or TestsFailed ? null : run.ExitCode;
                results.AddRange(Parse(app.AssemblyName, await File.ReadAllTextAsync(report, cancellationToken)));
            }
            finally
            {
                directory.Delete(recursive: true);
            }
        }

        return new TestRunReport(results, abnormalExitCode);
    }

    /// <summary>Escapes the characters VSTest's filter syntax reserves: <c>\ ( ) &amp; | ! = ~ ,</c>.</summary>
    public static string Escape(string value) => FilterSpecial().Replace(value, match => "\\" + match.Value);

    public static IReadOnlyList<ExecutedTest> Parse(string assembly, string trx)
    {
        XmlDocument document = new() { XmlResolver = null };
        document.LoadXml(trx);
        Dictionary<string, string> methods = new(StringComparer.Ordinal);
        foreach (XmlElement definition in document.GetElementsByTagName("UnitTest").OfType<XmlElement>())
        {
            if (definition.GetElementsByTagName("TestMethod").OfType<XmlElement>().FirstOrDefault() is XmlElement method)
            {
                methods[definition.GetAttribute("id")] = $"{method.GetAttribute("className")}.{method.GetAttribute("name")}";
            }
        }

        List<ExecutedTest> results = [];
        foreach (XmlElement result in document.GetElementsByTagName("UnitTestResult").OfType<XmlElement>())
        {
            if (!methods.TryGetValue(result.GetAttribute("testId"), out string? method))
            {
                continue;
            }

            TestOutcome outcome = result.GetAttribute("outcome") switch
            {
                "Passed" => TestOutcome.Pass,
                "Failed" => TestOutcome.Fail,
                "NotExecuted" => TestOutcome.Skip,
                _ => TestOutcome.NotRun,
            };
            string? message = outcome == TestOutcome.Fail ? result.GetElementsByTagName("Message").OfType<XmlElement>().FirstOrDefault()?.InnerText : null;
            (string? frame, string? file) = outcome == TestOutcome.Fail
                ? NativeTestRunner.TopFrame(result.GetElementsByTagName("StackTrace").OfType<XmlElement>().FirstOrDefault()?.InnerText)
                : (null, null);
            results.Add(new ExecutedTest($"{assembly}:{method}", result.GetAttribute("testName"), outcome, ExceptionTypeOf(message), frame, file));
        }

        return results;
    }

    /// <summary>"System.NotImplementedException : …" → the type; "Assert.Equal() Failure…" → an assertion; anything else → unknown.</summary>
    private static string? ExceptionTypeOf(string? message) => message is null
        ? null
        : ExceptionPrefix().Match(message) is { Success: true } match ? match.Groups["type"].Value
        : AssertionMessage().IsMatch(message) ? AssertionException
        : null;

    [GeneratedRegex(@"^(?<type>(?:[A-Za-z_][\w`]*[.+])*[A-Za-z_][\w`]*Exception) : ")]
    private static partial Regex ExceptionPrefix();

    [GeneratedRegex(@"^Assert\.\w+\(\) Failure")]
    private static partial Regex AssertionMessage();

    [GeneratedRegex(@"[\\()&|!=~,]")]
    private static partial Regex FilterSpecial();
}
