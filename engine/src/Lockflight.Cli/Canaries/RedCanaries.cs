using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Canaries;

/// <summary>
/// A red whose test fails by <c>Assert.Fail</c> must be rejected (RED_IN_TEST): it fails whatever the production code does. Judged as
/// <c>redgreen red</c> judges a run; the harmless twin, an assertion on production code's result, must be accepted (RED_ASSERT).
/// The full mode runs a real red (<see cref="CanaryProject"/>).
/// </summary>
internal static class RedCanaries
{
    private const string TestClass = "Canary.Tests.CanaryTests";

    public const string Name = "red: a test failing by Assert.Fail is rejected";

    public static CanaryReport Run()
    {
        RedClass twin = Classify("Xunit.Sdk.EqualException", "Canary.Tests.CanaryTests.CountsTheLines()");
        if (twin != RedClass.RedAssert)
        {
            return new CanaryReport(Name, CanaryResult.Broken, $"an assertion on production code's result is {RedClassNames.ToName(twin)}, not RED_ASSERT, so a rejection proves nothing");
        }

        RedClass planted = Classify("Xunit.Sdk.FailException", "Canary.Tests.CanaryTests.FailsUnconditionally()");
        return RedClassNames.IsAccepted(planted)
            ? new CanaryReport(Name, CanaryResult.Missed, $"accepted as {RedClassNames.ToName(planted)}")
            : new CanaryReport(Name, CanaryResult.Caught, $"rejected as {RedClassNames.ToName(planted)}");
    }

    private static RedClass Classify(string exceptionType, string topFrame) => RedClassifier.Classify(
        [new ExecutedTest($"Canary.Tests:{TestClass}.Planted", $"{TestClass}.Planted", TestOutcome.Fail, exceptionType, topFrame)],
        new HashSet<string>([TestClass], StringComparer.Ordinal), [], ProductionCode.None, callsProduction: true);
}
