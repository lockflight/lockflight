using System.Xml;
using Lockflight.Cli.Gates.Parsers;

namespace Lockflight.Cli.Testing;

/// <summary>
/// Reads a TRX report (VSTest's <c>--logger trx</c>, or Microsoft.Testing.Platform's <c>--report-trx</c>): one result per executed
/// case (parameterized rows included), keyed by the test definition's class and method, with each failure's exception type (read
/// from its message by the framework's <see cref="TrxFailureMessages"/>) and first stack frame. NUnit writes a parameterized
/// fixture's arguments into the class (<c>Shop.Tests.Prices(2)</c>) and a test case's into the method (<c>Costs(1,2)</c>); both are
/// dropped from the key, so every row and fixture instance keys to the method the listing names.
/// </summary>
internal static class TrxTestResults
{
    public static IReadOnlyList<ExecutedTest> Parse(string assembly, string trx, Func<string, string?> exceptionTypeOf)
    {
        XmlDocument document = new() { XmlResolver = null };
        document.LoadXml(trx);
        Dictionary<string, string> methods = new(StringComparer.Ordinal);
        foreach (XmlElement definition in document.GetElementsByTagName("UnitTest").OfType<XmlElement>())
        {
            if (definition.GetElementsByTagName("TestMethod").OfType<XmlElement>().FirstOrDefault() is XmlElement method)
            {
                methods[definition.GetAttribute("id")] = $"{WithoutArguments(method.GetAttribute("className"))}.{WithoutArguments(method.GetAttribute("name"))}";
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
                // A test that timed out, was aborted or errored did not pass: it must never read as "not run", which nothing counts.
                string abnormal when TrxOutcomes.IsAbnormal(abnormal) => TestOutcome.Fail,
                _ => TestOutcome.NotRun,
            };
            string? message = outcome == TestOutcome.Fail ? result.GetElementsByTagName("Message").OfType<XmlElement>().FirstOrDefault()?.InnerText : null;
            string? stackTrace = outcome == TestOutcome.Fail ? result.GetElementsByTagName("StackTrace").OfType<XmlElement>().FirstOrDefault()?.InnerText : null;
            (string? frame, string? file) = NativeTestRunner.TopFrame(stackTrace);
            results.Add(new ExecutedTest($"{assembly}:{method}", result.GetAttribute("testName"), outcome, message is null ? null : exceptionTypeOf(message), frame, file)
            {
                TopFrameLine = NativeTestRunner.TopFrameLine(stackTrace),
            });
        }

        return results;
    }

    /// <summary><c>Costs(1,2)</c> → <c>Costs</c>, <c>Prices(2)</c> → <c>Prices</c>, <c>Box&lt;Int32&gt;</c> → <c>Box</c>; a name without them unchanged.</summary>
    public static string WithoutArguments(string name)
    {
        int arguments = name.IndexOfAny(['(', '<']);
        return arguments > 0 ? name[..arguments] : name;
    }
}
