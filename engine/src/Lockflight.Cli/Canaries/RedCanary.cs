using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Canaries;

/// <summary>
/// The self-test's red canary: a red whose test fails by <c>Assert.Fail</c> must be classified RED_IN_TEST and rejected (it fails
/// whatever the production code does). Judged as <c>redgreen red</c> judges a run; the harmless twin, an assertion on production
/// code's result, must be accepted (RED_ASSERT). The engine's canary suite also runs a real red on a generated project.
/// </summary>
internal static class RedCanary
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
