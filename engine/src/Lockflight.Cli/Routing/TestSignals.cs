using System.Text.RegularExpressions;
using Lockflight.Cli.Config;
using Lockflight.Cli.Review;

namespace Lockflight.Cli.Routing;

/// <summary>
/// Tests weakened or bypassed: a test-run setting changed (<c>tests.configChanged</c>), a test file deleted or a test source file
/// holding fewer test attributes (<c>tests.removed</c>), any code line an existing test file no longer has as code
/// (<c>tests.changed</c>: an assertion taken out, an expected value edited, a line commented out; for test data any removed line),
/// and a skip marker or an <c>#if</c> added to test source (<c>tests.skipped</c>). Each raises to <c>behaviour</c>; all heuristics.
/// </summary>
internal sealed partial class TestSignals : IChangeSignalSource
{
    private const string VerifiedSnapshot = ".verified.";

    private static readonly string[] TestSourceSuffixes = [".cs", ".fs", ".vb"];

    public IEnumerable<ChangeSignal> Find(ChangeAnalysis analysis, RoutingPolicy policy)
    {
        foreach (RoutedFile file in analysis.Files)
        {
            if (file.Role == FileRole.TestConfig && file.Move?.OldPath != file.Path)
            {
                yield return Signal(SignalIds.TestsConfigChanged, file.Path, "a test-run or coverage setting changed");
            }

            if (file.Role != FileRole.Test || file.Move?.OldPath == file.Path || file.Move?.Kind == MoveKind.Exact)
            {
                continue;
            }

            foreach (ChangeSignal signal in TestFile(analysis, file))
            {
                yield return signal;
            }
        }
    }

    private static IEnumerable<ChangeSignal> TestFile(ChangeAnalysis analysis, RoutedFile file)
    {
        string basePath = file.Move?.OldPath ?? file.Path;
        bool source = IsTestSource(file.Path);
        if (file.Change.Kind == ChangeKind.Deleted)
        {
            yield return Signal(SignalIds.TestsRemoved, file.Path, source ? "test file deleted" : "test data file deleted");
            yield break;
        }

        string? baseText = file.Change.ExistsAtBase || file.Move is not null ? analysis.ChangeSet.Base.TextOf(basePath) : null;
        string? headText = analysis.ChangeSet.Head.TextOf(file.Path);
        if (source && baseText is not null && headText is not null && TestAttributes(headText) < TestAttributes(baseText))
        {
            yield return Signal(SignalIds.TestsRemoved, file.Path, $"fewer test attributes ({TestAttributes(baseText)} → {TestAttributes(headText)})");
        }

        (IReadOnlyList<RoutedLine> added, IReadOnlyList<RoutedLine> removed) = EffectiveLines.Of(analysis, file);
        if ((file.Change.Kind == ChangeKind.Modified || file.Move is not null) && Weakened(file.Path, baseText, headText, removed) is int line)
        {
            yield return Signal(SignalIds.TestsChanged, $"{basePath}:{line}", "a line of an existing test file removed, edited or commented out");
        }

        if (source && added.FirstOrDefault(line => SkipMarker().IsMatch(CSharpMask.Mask(line.Code)) || RawSkipMarker().IsMatch(line.Text)) is RoutedLine skip)
        {
            yield return Signal(SignalIds.TestsSkipped, $"{file.Path}:{skip.Line}", "a test skip, ignore or conditional-compilation marker added");
        }
    }

    /// <summary>
    /// The base line of the first code line the head no longer has as code: removed, edited, or still there but now inside a
    /// comment. For C# both sides are compared as multisets of code lines; for other files (test data, expected values) any removed
    /// non-blank line counts. Null when nothing was taken away.
    /// </summary>
    private static int? Weakened(string path, string? baseText, string? headText, IReadOnlyList<RoutedLine> removed)
    {
        if (baseText is null || headText is null || !ChangeAnalyzer.IsCSharp(path))
        {
            return removed.FirstOrDefault(line => line.Text.Trim().Length > 0)?.Line;
        }

        string[] baseLines = baseText.Split('\n');
        string[] baseCode = CommentMask.Of(baseText).Split('\n');
        Dictionary<string, int> headCode = new(StringComparer.Ordinal);
        string[] headLines = headText.Split('\n');
        string[] headMasked = CommentMask.Of(headText).Split('\n');
        for (int index = 0; index < headLines.Length; index++)
        {
            if (headMasked[index].Trim().Length > 0)
            {
                string key = WhitespacePairs.Collapse(headLines[index]);
                headCode[key] = headCode.GetValueOrDefault(key) + 1;
            }
        }

        for (int index = 0; index < baseLines.Length; index++)
        {
            if (baseCode[index].Trim().Length == 0)
            {
                continue;
            }

            string key = WhitespacePairs.Collapse(baseLines[index]);
            if (headCode.GetValueOrDefault(key) == 0)
            {
                return index + 1;
            }

            headCode[key]--;
        }

        return null;
    }

    /// <summary>
    /// Whether a file of a test project holds tests or their expected values: C#, F# or Visual Basic source, or an approved
    /// snapshot (<c>*.verified.*</c>); other data and fixtures the tests read are not judged as tests.
    /// </summary>
    private static bool IsTestSource(string path) =>
        TestSourceSuffixes.Any(suffix => path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        || ProjectIndex.FileName(path).Contains(VerifiedSnapshot, StringComparison.OrdinalIgnoreCase);

    private static int TestAttributes(string text) => TestAttribute().Count(CSharpMask.Mask(text));

    private static ChangeSignal Signal(string id, string where, string detail) => new(id, ChangeClass.Behaviour, SignalStrength.Heuristic, where, detail);

    [GeneratedRegex(@"\[\s*(?:[\w.]+\.)?(?:Fact|Theory|Test|TestCase|TestCaseSource|TestMethod|DataTestMethod|InlineData|DataRow|MemberData|ClassData)(?:Attribute)?\b", RegexOptions.CultureInvariant)]
    private static partial Regex TestAttribute();

    [GeneratedRegex(@"\b(?:Skip|Explicit)\s*=|\bSkip\.\w+|\[\s*(?:Ignore|Explicit)\b|\bAssert\.(?:Inconclusive|Ignore|Skip\w*)\b|\[\s*Fact\s*\(\s*Skip", RegexOptions.CultureInvariant)]
    private static partial Regex SkipMarker();

    [GeneratedRegex(@"^\s*#\s*(?:if|elif)\b|\[\s*Trait\s*\(\s*""Category""\s*,\s*""Skip", RegexOptions.CultureInvariant)]
    private static partial Regex RawSkipMarker();
}
