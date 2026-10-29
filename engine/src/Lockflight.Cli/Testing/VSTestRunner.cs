using System.Text.RegularExpressions;
using Lockflight.Cli.Gates;

namespace Lockflight.Cli.Testing;

/// <summary>
/// Runs tests of a VSTest (xUnit v2) test assembly selected by method — <c>dotnet test &lt;dll&gt; --filter
/// "FullyQualifiedName=A|FullyQualifiedName=B"</c> in batches — and reads the TRX (<see cref="TrxTestResults"/>). An xUnit v2
/// assertion prints no exception type but a message starting <c>Assert.Equal() Failure</c>; it is reported as
/// <c>Xunit.Sdk.XunitException</c>, the base of xUnit's assertion exceptions. A message that names neither an exception type nor
/// an assertion (a fixture or constructor failure) has no type, so the red classifier judges it by its stack, never as an assertion.
/// </summary>
internal sealed partial class VSTestRunner(TimeProvider time) : ITestRunner
{
    public const string AssertionException = TrxFailureMessages.XunitAssertion;

    private const int MethodsPerBatch = 40;

    public RunnerMode Mode => RunnerMode.VSTest;

    /// <summary>The tests from the assembly's metadata (xUnit v2 attributes): VSTest's own listing runs a test host for nothing.</summary>
    public Task<TestListing> ListAsync(TestApp app, string workingDirectory, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        try
        {
            return Task.FromResult(TestListing.Of(AssemblyTestMetadata.Read(app.AssemblyPath), RunnerMode.VSTest));
        }
        catch (Exception exception) when (exception is BadImageFormatException or IOException or InvalidOperationException)
        {
            return Task.FromResult(TestListing.NotListed);
        }
    }

    public async Task<TestRunOutcome> RunAsync(
        TestApp app, IReadOnlyList<string>? methodFilters, string workingDirectory, TimeSpan timeLimit, CancellationToken cancellationToken) =>
        TestRunOutcome.Of(await TrxTestRun.RunAsync(
            app, TestRunnerKind.VSTest, FilterBatches(methodFilters), TrxFailureMessages.XunitV2, workingDirectory, timeLimit, time, cancellationToken));

    /// <summary>One <c>--filter</c> per batch of methods; no arguments (the whole suite) when <paramref name="methodFilters"/> is null.</summary>
    public static IEnumerable<IReadOnlyList<string>> FilterBatches(IReadOnlyList<string>? methodFilters) => methodFilters is null
        ? [[]]
        : methodFilters.Chunk(MethodsPerBatch).Select(batch => (IReadOnlyList<string>)["--filter", string.Join('|', batch.Select(method => $"FullyQualifiedName={Escape(method)}"))]);

    /// <summary>Escapes the characters VSTest's filter syntax reserves: <c>\ ( ) &amp; | ! = ~ ,</c>.</summary>
    public static string Escape(string value) => FilterSpecial().Replace(value, match => "\\" + match.Value);

    public static IReadOnlyList<ExecutedTest> Parse(string assembly, string trx) => TrxTestResults.Parse(assembly, trx, TrxFailureMessages.XunitV2);

    [GeneratedRegex(@"[\\()&|!=~,]")]
    private static partial Regex FilterSpecial();
}
