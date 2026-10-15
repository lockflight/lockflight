using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// Classifies a new test from its pre-implementation run. A failure counts as a stub red only when the exception's top stack frame
/// is outside the test code — a test (or a helper or lambda in it) that throws <c>NotImplementedException</c> itself proves nothing.
/// Test code is recognised by the frame's source file under a test project, or, without symbols, by its declaring type. Runtime
/// evidence, not proof of history: the review judges assertion quality.
/// </summary>
internal static class RedClassifier
{
    public static RedClass Classify(IReadOnlyList<ExecutedTest> rows, IReadOnlySet<string> testClasses, IReadOnlyList<string> testDirectories)
    {
        if (rows.Count == 0 || rows.Any(row => row.Outcome is TestOutcome.Skip or TestOutcome.NotRun))
        {
            return RedClass.Skipped;
        }

        if (rows.Any(row => row.Outcome == TestOutcome.Pass))
        {
            return RedClass.GreenBefore;
        }

        RedClass[] reds = [.. rows.Select(row => ClassifyFailure(row, testClasses, testDirectories))];
        return reds.Contains(RedClass.RedInTest) ? RedClass.RedInTest
            : reds.Contains(RedClass.RedOther) ? RedClass.RedOther
            : reds.Contains(RedClass.RedAssert) ? RedClass.RedAssert
            : RedClass.RedStub;
    }

    private static RedClass ClassifyFailure(ExecutedTest row, IReadOnlySet<string> testClasses, IReadOnlyList<string> testDirectories)
    {
        if (row.ExceptionType?.StartsWith("Xunit.Sdk.", StringComparison.Ordinal) == true)
        {
            return RedClass.RedAssert;
        }

        if (row.TopFrame is null || IsInTestFile(row.TopFrameFile, testDirectories) || IsInTestClass(row.TopFrame, testClasses))
        {
            return RedClass.RedInTest;
        }

        return row.ExceptionType == "System.NotImplementedException" ? RedClass.RedStub : RedClass.RedOther;
    }

    private static bool IsInTestFile(string? file, IReadOnlyList<string> testDirectories)
    {
        if (file is null)
        {
            return false;
        }

        string normalized = file.Replace('\\', '/');
        return testDirectories.Any(directory => normalized.StartsWith(directory.Replace('\\', '/').TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Stack frames print nested types with '.' and closures as compiler-generated segments
    /// (<c>Shop.Tests.Outer.&lt;&gt;c.&lt;Test&gt;b__1_0()</c>): drop the generated segments, then the frame is test code when its
    /// declaring type is a test class or nested in one.
    /// </summary>
    private static bool IsInTestClass(string frame, IReadOnlySet<string> testClasses)
    {
        string[] segments = [.. frame.Split('(')[0].Split('.').Where(segment => !segment.StartsWith('<'))];
        string type = string.Join('.', segments.Take(segments.Length - 1));
        return testClasses.Select(testClass => testClass.Replace('+', '.'))
            .Any(testClass => type == testClass || type.StartsWith(testClass + ".", StringComparison.Ordinal) || frame.StartsWith(testClass + ".<", StringComparison.Ordinal));
    }
}
