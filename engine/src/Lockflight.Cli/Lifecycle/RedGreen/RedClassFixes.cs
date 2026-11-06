namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>The advice red/green prints for each way a new test can be rejected.</summary>
internal static class RedClassFixes
{
    /// <summary>The advice for a RED_OTHER that failed while loading XAML: a view is no place for a red.</summary>
    public const string XamlLoading =
        "the failure is in loading XAML (a missing resource, type or name in the view), not in production code; test the ViewModel instead, or stub a member the view calls and assert on what it returns";

    /// <summary>What to change when a new test is rejected with this class.</summary>
    public static string For(RedClass redClass) => redClass switch
    {
        RedClass.RedOther => "the failure is not the stub's NotImplementedException thrown by production code; declare the member as a stub, or assert on the value it returns",
        RedClass.RedInTest => "the test (or its fixture, constructor or Assert.Fail) fails by itself; make it call the production code and fail there",
        RedClass.RedNoProduction => "the assertion fails without the test calling production code; call the member under test and assert on its result",
        RedClass.GreenBefore => "the test passes before the implementation exists; it must describe behaviour that is still missing",
        RedClass.Skipped => "the test is skipped or did not run; remove Skip, Explicit or Ignore (an Assert.Skip, Assert.Ignore or Assert.Inconclusive too)",
        RedClass.Flaky => "repeated runs disagree; make the test deterministic",
        _ => string.Empty,
    };

    /// <summary>What to change when new tests are rejected with this class; RED_OTHER adds the XAML advice when one of them failed loading XAML.</summary>
    public static string For(RedClass redClass, IEnumerable<RedVerdict> verdicts) =>
        redClass == RedClass.RedOther && verdicts.Any(verdict => verdict.Class == RedClass.RedOther && verdict.InXamlLoading)
            ? $"{For(redClass)}; {XamlLoading}"
            : For(redClass);
}
