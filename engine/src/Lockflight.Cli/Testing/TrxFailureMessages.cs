using System.Text.RegularExpressions;

namespace Lockflight.Cli.Testing;

/// <summary>
/// The exception type a failure's TRX message names, per framework; a TRX result has no exception type of its own. An unexpected
/// exception is printed as <c>Type : message</c> (xUnit v2, NUnit) or after <c>Test method … threw exception:</c> (MSTest). An
/// assertion is recognised by its framework's wording and reported as the framework's assertion exception: xUnit v2's
/// <c>Assert.Equal() Failure</c>, NUnit's <c>Expected: … But was: …</c> lines and <c>Multiple failures or warnings in test:</c>,
/// MSTest's <c>Assert.AreEqual failed.</c> (MSTest 3) or <c>Assertion failed. Expected …</c> (MSTest 4). <c>Assert.Fail</c> fails
/// whatever the production code does, so its wording (an NUnit message without those lines, MSTest's <c>Assert.Fail failed.</c> or
/// a bare <c>Assertion failed.</c>) names no type, like a fixture failure: the red classifier then judges it by its stack, never as
/// an assertion.
/// </summary>
internal static partial class TrxFailureMessages
{
    public const string XunitAssertion = "Xunit.Sdk.XunitException";

    public const string NUnitAssertion = "NUnit.Framework.AssertionException";

    public const string NUnitMultipleAssertion = "NUnit.Framework.MultipleAssertException";

    public const string MSTestAssertion = "Microsoft.VisualStudio.TestTools.UnitTesting.AssertFailedException";

    public const string MSTestInconclusive = "Microsoft.VisualStudio.TestTools.UnitTesting.AssertInconclusiveException";

    private const string MSTestFailMethod = "Fail";

    private const string MSTestInconclusiveMethod = "Inconclusive";

    /// <summary>"System.NotImplementedException : …" → the type; "Assert.Equal() Failure…" → an assertion; anything else → unknown.</summary>
    public static string? XunitV2(string message) =>
        ExceptionPrefix().Match(message) is { Success: true } match ? match.Groups["type"].Value
        : XunitAssertionMessage().IsMatch(message) ? XunitAssertion
        : null;

    /// <summary>"System.NotImplementedException : …" → the type; NUnit's assertion wording → its assertion exceptions; anything else → unknown.</summary>
    public static string? NUnit(string message) =>
        ExceptionPrefix().Match(message) is { Success: true } match ? match.Groups["type"].Value
        : message.StartsWith("Multiple failures or warnings in test:", StringComparison.Ordinal) ? NUnitMultipleAssertion
        : NUnitExpected().IsMatch(message) && NUnitButWas().IsMatch(message) ? NUnitAssertion
        : null;

    /// <summary>"Test method … threw exception:" then the type; MSTest's assertion wording → its assertion exception; anything else → unknown.</summary>
    public static string? MSTest(string message)
    {
        if (MSTestThrew().Match(message) is { Success: true } threw)
        {
            return threw.Groups["type"].Value;
        }

        if (MSTestNamedAssertion().Match(message) is { Success: true } named)
        {
            return named.Groups["method"].Value switch
            {
                MSTestFailMethod => null,
                MSTestInconclusiveMethod => MSTestInconclusive,
                _ => MSTestAssertion,
            };
        }

        if (message.StartsWith("Assert.Inconclusive.", StringComparison.Ordinal))
        {
            return MSTestInconclusive;
        }

        return MSTestAssertionWithWording().IsMatch(message) ? MSTestAssertion : null;
    }

    [GeneratedRegex(@"^(?<type>(?:[A-Za-z_][\w`]*[.+])*[A-Za-z_][\w`]*Exception) : ")]
    private static partial Regex ExceptionPrefix();

    [GeneratedRegex(@"^Assert\.\w+\(\) Failure")]
    private static partial Regex XunitAssertionMessage();

    [GeneratedRegex(@"^\s*Expected:", RegexOptions.Multiline)]
    private static partial Regex NUnitExpected();

    [GeneratedRegex(@"^\s*But was:", RegexOptions.Multiline)]
    private static partial Regex NUnitButWas();

    [GeneratedRegex(@"^Test method \S+ threw exception:\s*\r?\n(?<type>(?:[A-Za-z_][\w`]*[.+])*[A-Za-z_][\w`]*Exception): ")]
    private static partial Regex MSTestThrew();

    [GeneratedRegex(@"^(?:Assert|CollectionAssert|StringAssert)\.(?<method>\w+) failed\.")]
    private static partial Regex MSTestNamedAssertion();

    /// <summary>MSTest 4: <c>Assertion failed.</c> followed on the same line by what was expected; <c>Assert.Fail</c> puts its message on the next line.</summary>
    [GeneratedRegex(@"^Assertion failed\.[^\S\r\n]*[^\s]")]
    private static partial Regex MSTestAssertionWithWording();
}
