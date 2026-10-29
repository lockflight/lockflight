using System.Xml.Linq;
using Lockflight.Cli.Gates;

namespace Lockflight.Cli.Testing;

/// <summary>
/// NUnit through VSTest or on Microsoft.Testing.Platform: tests listed from assembly metadata (<see cref="AssemblyTestMetadata"/>),
/// run with a run-settings file whose NUnit <c>Where</c> selects the methods by class and method — a <c>FullyQualifiedName</c> filter
/// misses the tests of a parameterized fixture (<c>Shop.Tests.Prices(2).Costs</c>) and runs an <c>[Explicit]</c> test it names —
/// and read from TRX. NUnit prints no exception type for an assertion; <see cref="TrxFailureMessages.NUnit"/> recognises its wording.
/// </summary>
internal sealed class NUnitRunner(TimeProvider time, TestRunnerKind platform) : ITestRunner
{
    public RunnerMode Mode => platform == TestRunnerKind.MicrosoftTestingPlatform ? RunnerMode.NUnitMtp : RunnerMode.NUnit;

    public Task<TestListing> ListAsync(TestApp app, string workingDirectory, TimeSpan timeLimit, CancellationToken cancellationToken) =>
        Task.FromResult(TrxTestListing.List(app, platform, TestFrameworkKind.NUnit, Mode));

    public async Task<TestRunOutcome> RunAsync(
        TestApp app, IReadOnlyList<string>? methodFilters, TraitCondition? suiteFilter, string workingDirectory, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        if (methodFilters is { Count: 0 })
        {
            return TestRunOutcome.Of(new TestRunReport([], AbnormalExitCode: null));
        }

        string? settings = methodFilters is null ? null : Path.Combine(Path.GetTempPath(), $"lockflight-nunit-{Guid.CreateVersion7()}.runsettings");
        try
        {
            if (settings is not null)
            {
                await File.WriteAllTextAsync(settings, RunSettings(methodFilters!), cancellationToken);
            }

            IReadOnlyList<string> selection = settings is null ? VSTestRunner.SuiteArguments(suiteFilter) : ["--settings", settings];
            return TestRunOutcome.Of(await TrxTestRun.RunAsync(app, platform, [selection], TrxFailureMessages.NUnit, workingDirectory, timeLimit, time, cancellationToken));
        }
        finally
        {
            if (settings is not null)
            {
                File.Delete(settings);
            }
        }
    }

    /// <summary>The run settings selecting the given <c>Class.Method</c> names.</summary>
    public static string RunSettings(IReadOnlyList<string> methodFilters) =>
        new XElement("RunSettings", new XElement("NUnit", new XElement("Where", Where(methodFilters)))).ToString();

    /// <summary><c>(class == "A.B" &amp;&amp; method == "C") || …</c>: NUnit's test selection language, with quoted values.</summary>
    public static string Where(IReadOnlyList<string> methodFilters) => string.Join(" || ", methodFilters.Select(filter =>
    {
        int dot = filter.LastIndexOf('.');
        return $"(class == {Quote(filter[..dot])} && method == {Quote(filter[(dot + 1)..])})";
    }));

    private static string Quote(string value) => $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
