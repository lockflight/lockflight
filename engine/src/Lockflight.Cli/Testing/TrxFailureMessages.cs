using System.Text.RegularExpressions;

namespace Lockflight.Cli.Testing;

/// <summary>
/// The exception type a failure's TRX message names, per framework; a TRX result has no exception type of its own. An unexpected
/// exception is printed as <c>Type : message</c>. An assertion is recognised by its framework's wording and reported as the
/// framework's assertion exception: xUnit v2's <c>Assert.Equal() Failure</c>, NUnit's <c>Expected: … But was: …</c> lines and
/// <c>Multiple failures or warnings in test:</c>. <c>Assert.Fail</c> fails whatever the production code does, so its wording (an
/// NUnit message without those lines) names no type, like a fixture failure: the red classifier then judges it by its stack, never
/// as an assertion.
/// </summary>
internal static partial class TrxFailureMessages
{
    public const string XunitAssertion = "Xunit.Sdk.XunitException";

    public const string NUnitAssertion = "NUnit.Framework.AssertionException";

    public const string NUnitMultipleAssertion = "NUnit.Framework.MultipleAssertException";

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

    [GeneratedRegex(@"^(?<type>(?:[A-Za-z_][\w`]*[.+])*[A-Za-z_][\w`]*Exception) : ")]
    private static partial Regex ExceptionPrefix();

    [GeneratedRegex(@"^Assert\.\w+\(\) Failure")]
    private static partial Regex XunitAssertionMessage();

    [GeneratedRegex(@"^\s*Expected:", RegexOptions.Multiline)]
    private static partial Regex NUnitExpected();

    [GeneratedRegex(@"^\s*But was:", RegexOptions.Multiline)]
    private static partial Regex NUnitButWas();
}
