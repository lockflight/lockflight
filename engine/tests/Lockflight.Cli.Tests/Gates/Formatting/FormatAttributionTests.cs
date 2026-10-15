using Lockflight.Cli.Gates.Formatting;

namespace Lockflight.Cli.Tests.Gates.Formatting;

public sealed class FormatAttributionTests
{
    private const string File = "src/A.cs";

    // Line 3 ends in three trailing spaces; line 4 is indented by two spaces instead of four.
    private const string Text = "namespace A;\n\npublic class B {   \n  int x;\n    int y;\n}\n";

    [Fact]
    public void AnEndOfLineDifferenceIsNeverAttributed()
    {
        FormatJudgement judgement = Judge(new FormatDiagnostic(File, 1, 13, "ENDOFLINE", "Fix end of line marker. Replace 1 characters with '\\r\\n'."), changedLines: [1]);

        Assert.Empty(judgement.Attributed);
        Assert.Equal(1, judgement.EndOfLineOnly);
    }

    [Fact]
    public void AWhitespaceDiagnosticThatOnlyAddsCarriageReturnsIsAnEndOfLineDifference()
    {
        FormatJudgement judgement = Judge(new FormatDiagnostic(File, 4, 9, "WHITESPACE", "Fix whitespace formatting. Replace 5 characters with '\\r\\n\\s\\s\\s\\s'."), changedLines: [5]);

        Assert.Empty(judgement.Attributed);
        Assert.Equal(1, judgement.EndOfLineOnly);
    }

    [Fact]
    public void BadIndentationOnAChangedLineIsAttributed()
    {
        FormatDiagnostic diagnostic = new(File, 3, 17, "WHITESPACE", "Fix whitespace formatting. Replace 6 characters with '\\r\\n\\s\\s\\s\\s'.");

        FormatJudgement judgement = Judge(diagnostic, changedLines: [4]);

        Assert.Equal([new FormatFinding(File, 4, "WHITESPACE")], judgement.Attributed);
    }

    [Fact]
    public void TrailingSpacesOnAnUntouchedLineDoNotImplicateTheChangedLineAfterIt()
    {
        FormatDiagnostic diagnostic = new(File, 3, 17, "WHITESPACE", "Fix whitespace formatting. Replace 6 characters with '\\r\\n\\s\\s'.");

        FormatJudgement judgement = Judge(diagnostic, changedLines: [4]);

        Assert.Empty(judgement.Attributed);
        Assert.Equal(1, judgement.Untouched);
    }

    [Fact]
    public void ADiagnosticOnAnUntouchedLineIsCountedButNotAttributed()
    {
        FormatJudgement judgement = Judge(new FormatDiagnostic(File, 3, 17, "WHITESPACE", "Fix whitespace formatting. Replace 6 characters with '\\n\\s\\s\\s\\s'."), changedLines: [5]);

        Assert.Empty(judgement.Attributed);
        Assert.Equal(1, judgement.Untouched);
    }

    [Fact]
    public void ADescriptionThatCannotBeReadJudgesTheWholeFile()
    {
        FormatDiagnostic diagnostic = new(File, 5, 1, "WHITESPACE", "Something new.");

        FormatJudgement judgement = Judge(diagnostic, changedLines: [1]);

        Assert.Equal([new FormatFinding(File, 5, "WHITESPACE")], judgement.Attributed);
        Assert.Equal([File], judgement.WholeFiles);
    }

    [Fact]
    public void AFileThatCannotBeReadIsJudgedWhole()
    {
        FormatDiagnostic diagnostic = new(File, 3, 16, "WHITESPACE", "Fix whitespace formatting. Replace 3 characters with ''.");

        FormatJudgement judgement = FormatAttribution.Judge([diagnostic], new Dictionary<string, IReadOnlySet<int>> { [File] = new HashSet<int> { 1 } }, _ => null, new HashSet<string>());

        Assert.Equal([File], judgement.WholeFiles);
    }

    [Fact]
    public void AnEncodingDifferenceTheChangeDidNotMakeIsNotAttributed()
    {
        FormatJudgement judgement = Judge(new FormatDiagnostic(File, 1, 1, FormatAttribution.CharsetDiagnostic, "Fix file encoding."), changedLines: [1, 3]);

        Assert.Empty(judgement.Attributed);
        Assert.Empty(judgement.WholeFiles);
        Assert.Equal(1, judgement.CharsetOnly);
    }

    [Fact]
    public void AnEncodingDifferenceInAFileWhoseEncodingTheChangeChangedIsAttributed()
    {
        FormatDiagnostic diagnostic = new(File, 1, 1, FormatAttribution.CharsetDiagnostic, "Fix file encoding.");

        FormatJudgement judgement = FormatAttribution.Judge(
            [diagnostic], new Dictionary<string, IReadOnlySet<int>> { [File] = new HashSet<int> { 1 } }, _ => Text, new HashSet<string> { File });

        Assert.Equal([new FormatFinding(File, 1, FormatAttribution.CharsetDiagnostic)], judgement.Attributed);
        Assert.Equal(0, judgement.CharsetOnly);
    }

    private static FormatJudgement Judge(FormatDiagnostic diagnostic, int[] changedLines) =>
        FormatAttribution.Judge([diagnostic], new Dictionary<string, IReadOnlySet<int>> { [File] = changedLines.ToHashSet() }, _ => Text, new HashSet<string>());
}
