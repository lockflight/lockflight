using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// Classifies a new test from its pre-implementation run. A red is accepted only when the failure comes from production code:
/// a <c>NotImplementedException</c> whose top stack frame is in production code (RED_STUB), or an xUnit assertion in a test that
/// calls production code (RED_ASSERT). Test code is recognised by the frame's source file under a test project, or, without
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

    public static RedClass Classify(
        IReadOnlyList<ExecutedTest> rows, IReadOnlySet<string> testClasses, IReadOnlyList<string> testDirectories, ProductionCode production, bool callsProduction)
    {
        if (rows.Count == 0 || rows.Any(row => row.Outcome is TestOutcome.Skip or TestOutcome.NotRun))
        {
            return RedClass.Skipped;
        }

        if (rows.Any(row => row.Outcome == TestOutcome.Pass))
        {
            return RedClass.GreenBefore;
        }

        RedClass[] reds = [.. rows.Select(row => ClassifyFailure(row, testClasses, testDirectories, production, callsProduction))];
        return reds.Contains(RedClass.RedInTest) ? RedClass.RedInTest
            : reds.Contains(RedClass.RedNoProduction) ? RedClass.RedNoProduction
            : reds.Contains(RedClass.RedOther) ? RedClass.RedOther
            : reds.Contains(RedClass.RedAssert) ? RedClass.RedAssert
            : RedClass.RedStub;
    }

    private static RedClass ClassifyFailure(
        ExecutedTest row, IReadOnlySet<string> testClasses, IReadOnlyList<string> testDirectories, ProductionCode production, bool callsProduction)
    {
        if (IsAssertion(row.ExceptionType))
        {
            return callsProduction ? RedClass.RedAssert : RedClass.RedNoProduction;
        }

        if (row.ExceptionType?.StartsWith(XunitNamespace, StringComparison.Ordinal) == true || row.TopFrame is null
            || IsInDirectory(row.TopFrameFile, testDirectories) || IsInType(row.TopFrame, testClasses.Select(testClass => testClass.Replace('+', '.'))))
        {
            return RedClass.RedInTest;
        }

        bool inProduction = IsInDirectory(row.TopFrameFile, production.ProjectDirectories) || IsInType(row.TopFrame, production.TypeNames);
        return inProduction && row.ExceptionType == "System.NotImplementedException" ? RedClass.RedStub : RedClass.RedOther;
    }

    private static bool IsAssertion(string? exceptionType) =>
        exceptionType is not null && exceptionType.StartsWith(XunitNamespace, StringComparison.Ordinal) && exceptionType.EndsWith("Exception", StringComparison.Ordinal)
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
