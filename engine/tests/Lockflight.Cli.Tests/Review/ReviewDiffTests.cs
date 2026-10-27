using Lockflight.Cli.Review;

namespace Lockflight.Cli.Tests.Review;

public sealed class ReviewDiffTests
{
    [Fact]
    public void ADiffWithinTheLimitIsShownWhole()
    {
        string diff = Section("src/Orders.cs", 10) + Section("src/Prices.cs", 10);

        ReviewDiff fitted = ReviewDiff.Fit(diff, diff.Length);

        Assert.True(fitted.IsComplete);
        Assert.Equal(diff, fitted.Shown);
        Assert.Equal(2, fitted.Files);
        Assert.Empty(fitted.NotShown);
    }

    [Fact]
    public void AnOverlongDiffShowsTheWholeFilesThatFitAndNamesTheRest()
    {
        string first = Section("src/Orders.cs", 10);
        string last = Section("src/Taxes.cs", 10);
        string diff = first + Section("src/Prices.cs", 200) + last;

        ReviewDiff fitted = ReviewDiff.Fit(diff, first.Length + last.Length + 50);

        Assert.False(fitted.IsComplete);
        Assert.Equal(diff.Length, fitted.Length);
        Assert.Equal(3, fitted.Files);
        Assert.StartsWith(first + last, fitted.Shown, StringComparison.Ordinal);
        Assert.Equal(["src/Prices.cs"], fitted.NotShown);
        Assert.Contains("1 of 3 changed files not shown in full; read them: src/Prices.cs", fitted.Shown[(first.Length + last.Length)..], StringComparison.Ordinal);
    }

    [Fact]
    public void AFirstFileLargerThanTheLimitIsCutAndNamed()
    {
        string diff = Section("src/Orders.cs", 500) + Section("src/Prices.cs", 500);

        ReviewDiff fitted = ReviewDiff.Fit(diff, 200);

        Assert.StartsWith(diff[..200], fitted.Shown, StringComparison.Ordinal);
        Assert.Equal(["src/Orders.cs", "src/Prices.cs"], fitted.NotShown);
    }

    [Fact]
    public void AUnitsFilesAreNamedWithTheUnitPath()
    {
        string diff = Section("src/Orders.cs", 10) + Section("billing/src/Invoices.cs", 500);

        ReviewDiff fitted = ReviewDiff.Fit(diff, 300);

        Assert.Equal(["billing/src/Invoices.cs"], fitted.NotShown);
    }

    [Fact]
    public void AQuotedFileNameIsNamedWithoutItsQuotes()
    {
        string diff = Section("src/Orders.cs", 10) + "diff --git \"a/src/Pr\\303\\251ces.cs\" \"b/src/Pr\\303\\251ces.cs\"\n" + new string('+', 500) + "\n";

        ReviewDiff fitted = ReviewDiff.Fit(diff, 300);

        Assert.Equal(["src/Pr\\303\\251ces.cs"], fitted.NotShown);
    }

    private static string Section(string path, int lines) =>
        $"diff --git a/{path} b/{path}\n--- a/{path}\n+++ b/{path}\n@@ -1 +1,{lines} @@\n" + string.Concat(Enumerable.Repeat("+line\n", lines));
}
