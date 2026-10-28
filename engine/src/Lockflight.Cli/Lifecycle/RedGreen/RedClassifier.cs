using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// Classifies a new test from its pre-implementation run. A red is accepted only when the failure comes from production code:
/// a <c>NotImplementedException</c> whose top stack frame is in production code (RED_STUB), or an assertion (xUnit's, or one of the
/// assertion and mocking libraries below) in a test that calls production code (RED_ASSERT). Test code is recognised by the frame's source file under a test project, or, without
/// symbols, by its declaring type; production code by the frame's file under a production project or its type in a production
/// assembly. Runtime and static evidence, not proof of history: the review judges assertion quality.
/// </summary>
internal static class RedClassifier
{
    private const string XunitNamespace = "Xunit.Sdk.";

    /// <summary>
    /// The exceptions of xUnit's assertions (v2 and v3). Other <c>Xunit.Sdk</c> exceptions are the test framework failing the test
    /// (a throwing fixture or constructor, a timeout) or <c>Assert.Fail</c>, which fails whatever the production code does.
    /// </summary>
    private static readonly HashSet<string> AssertionExceptions = new(
        [
            "All", "Collection", "Contains", "Distinct", "DoesNotContain", "DoesNotMatch", "Empty", "EndsWith", "Equal", "Equivalent",
            "False", "InRange", "IsAssignableFrom", "IsNotAssignableFrom", "IsNotType", "IsType", "Matches", "Multiple", "NotEmpty",
            "NotEqual", "NotInRange", "NotNull", "NotRaises", "NotSame", "NotStrictEqual", "Null", "ProperSubset", "ProperSuperset",
            "PropertyChanged", "Raises", "RaisesAny", "Same", "Single", "StartsWith", "StrictEqual", "Subset", "Superset", "Throws",
            "ThrowsAny", "True",
        ],
        StringComparer.Ordinal);

    /// <summary>
    /// The failure exceptions of assertion and mocking libraries, checked against the packages: <c>Xunit.Sdk.XunitException</c>
    /// (FluentAssertions and AwesomeAssertions throw it under xUnit, and an xUnit v2 assertion in a VSTest run reports it; xUnit itself
    /// never throws it bare), Shouldly's <c>Should…</c> failures, the two libraries' own exception without a test framework, a failed
    /// NSubstitute <c>Received</c> or <c>Received.InOrder</c>, a failed Moq verification (or an unexpected call on a strict mock), and
    /// a failed Verify snapshot. A library's other exceptions report its misuse, not the production code.
    /// </summary>
    private static readonly HashSet<string> LibraryAssertionExceptions = new(
        [
            "Xunit.Sdk.XunitException",
            "Shouldly.ShouldAssertException", "Shouldly.ShouldMatchApprovedException", "Shouldly.ShouldCompleteInException",
            "FluentAssertions.Execution.AssertionFailedException", "AwesomeAssertions.Execution.AssertionFailedException",
            "NSubstitute.Exceptions.ReceivedCallsException", "NSubstitute.Exceptions.CallSequenceNotFoundException",
            "Moq.MockException",
            "VerifyException",
        ],
        StringComparer.Ordinal);

    /// <summary>
    /// Where FluentAssertions and AwesomeAssertions throw. A VSTest run prints their failure without its exception type, so the
    /// top frame identifies it.
    /// </summary>
    private static readonly string[] AssertionLibraryFrames = ["FluentAssertions.Execution.", "AwesomeAssertions.Execution."];

    /// <summary>The order in which one failing row decides a theory's class: the worst row wins.</summary>
    private static readonly RedClass[] Precedence = [RedClass.RedInTest, RedClass.RedNoProduction, RedClass.RedOther, RedClass.RedAssert, RedClass.RedStub];

    public static RedClass Classify(
        IReadOnlyList<ExecutedTest> rows, IReadOnlySet<string> testClasses, IReadOnlyList<string> testDirectories, ProductionCode production, bool callsProduction) =>
        Judge(rows, testClasses, testDirectories, production, callsProduction ? "production code" : null).Class;

    /// <param name="productionCall">The first production member the test reaches (<see cref="TestCodeScanner.ProductionCall"/>); null for none.</param>
    public static RedVerdict Judge(
        IReadOnlyList<ExecutedTest> rows, IReadOnlySet<string> testClasses, IReadOnlyList<string> testDirectories, ProductionCode production, string? productionCall)
    {
        string calls = productionCall is null ? "no production call found" : $"calls {productionCall}";
        if (rows.Count == 0 || rows.Any(row => row.Outcome is TestOutcome.Skip or TestOutcome.NotRun))
        {
            return new RedVerdict(RedClass.Skipped, "skipped or not run", calls);
        }

        if (rows.Any(row => row.Outcome == TestOutcome.Pass))
        {
            return new RedVerdict(RedClass.GreenBefore, "passed before the implementation exists", calls);
        }

        RedVerdict[] reds = [.. rows.Select(row => JudgeFailure(row, testClasses, testDirectories, production, productionCall))];
        return Precedence.Select(redClass => reds.FirstOrDefault(red => red.Class == redClass)).OfType<RedVerdict>().First();
    }

    private static RedVerdict JudgeFailure(
        ExecutedTest row, IReadOnlySet<string> testClasses, IReadOnlyList<string> testDirectories, ProductionCode production, string? productionCall)
    {
        string exception = ShortType(row.ExceptionType);
        string detail = $"{row.ExceptionType ?? "no exception type"} at {row.TopFrame ?? "no stack frame"}"
            + (row.TopFrameFile is null ? string.Empty : $" in {row.TopFrameFile}{(row.TopFrameLine is int line ? $":{line}" : string.Empty)}")
            + $"; {(productionCall is null ? "no production call found" : $"calls {productionCall}")}";
        if (IsAssertion(row.ExceptionType, row.TopFrame))
        {
            exception = row.ExceptionType is null ? "the assertion" : exception;
            return productionCall is null
                ? new RedVerdict(RedClass.RedNoProduction, $"{exception} fails, but no production call found: naming a production type (a type argument such as Deserialize<T>, typeof) is not a call; call a method or constructor, or read a property", detail)
                : new RedVerdict(RedClass.RedAssert, $"{exception} fails; calls {productionCall}", detail);
        }

        if (row.ExceptionType?.StartsWith(XunitNamespace, StringComparison.Ordinal) == true)
        {
            return new RedVerdict(RedClass.RedInTest, $"{exception} from the test framework (Assert.Fail, a throwing fixture or constructor, a timeout)", detail);
        }

        if (row.TopFrame is null)
        {
            return new RedVerdict(RedClass.RedInTest, $"{exception} with no stack frame to tell where it was thrown", detail);
        }

        if (IsInDirectory(row.TopFrameFile, testDirectories) || IsInType(row.TopFrame, testClasses.Select(testClass => testClass.Replace('+', '.'))))
        {
            return new RedVerdict(RedClass.RedInTest, $"{exception} thrown in test code at {ShortFrame(row.TopFrame)}", detail);
        }

        bool inProduction = IsInDirectory(row.TopFrameFile, production.ProjectDirectories) || IsInType(row.TopFrame, production.TypeNames);
        if (inProduction && row.ExceptionType == "System.NotImplementedException")
        {
            return new RedVerdict(RedClass.RedStub, $"NotImplementedException from production code at {ShortFrame(row.TopFrame)}", detail);
        }

        return new RedVerdict(
            RedClass.RedOther,
            inProduction
                ? $"{exception} from production code at {ShortFrame(row.TopFrame)}, not a NotImplementedException"
                : $"{exception} thrown outside production code, at {ShortFrame(row.TopFrame)}",
            detail);
    }

    /// <summary><c>System.NotImplementedException</c> → <c>NotImplementedException</c>; "an exception" when the type is unknown.</summary>
    private static string ShortType(string? exceptionType) => exceptionType is null ? "an exception" : exceptionType.Split('.')[^1];

    /// <summary><c>Shop.Api.PriceCalculator.ShippingCost(Decimal w)</c> → <c>PriceCalculator.ShippingCost</c>.</summary>
    private static string ShortFrame(string frame) => TraceMatrix.ShortName(frame.Split('(')[0]);

    private static bool IsAssertion(string? exceptionType, string? topFrame) => exceptionType is null
        ? topFrame is not null && AssertionLibraryFrames.Any(frame => topFrame.StartsWith(frame, StringComparison.Ordinal))
        : LibraryAssertionExceptions.Contains(exceptionType) || IsXunitAssertion(exceptionType);

    private static bool IsXunitAssertion(string exceptionType) =>
        exceptionType.StartsWith(XunitNamespace, StringComparison.Ordinal) && exceptionType.EndsWith("Exception", StringComparison.Ordinal)
        && AssertionExceptions.Contains(exceptionType[XunitNamespace.Length..^"Exception".Length]);

    private static bool IsInDirectory(string? file, IReadOnlyList<string> directories)
    {
        if (file is null)
        {
            return false;
        }

        string normalized = file.Replace('\\', '/');
        return directories.Any(directory => normalized.StartsWith(directory.Replace('\\', '/').TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Stack frames print nested types with '.' and closures as compiler-generated segments
    /// (<c>Shop.Tests.Outer.&lt;&gt;c.&lt;Test&gt;b__1_0()</c>): drop the generated segments, then the frame is in one of the given
    /// types when its declaring type is that type or nested in it.
    /// </summary>
    private static bool IsInType(string frame, IEnumerable<string> types)
    {
        string[] segments = [.. frame.Split('(')[0].Split('.').Where(segment => !segment.StartsWith('<'))];
        string type = string.Join('.', segments.Take(segments.Length - 1));
        return types.Any(candidate => type == candidate || type.StartsWith(candidate + ".", StringComparison.Ordinal) || frame.StartsWith(candidate + ".<", StringComparison.Ordinal));
    }
}
