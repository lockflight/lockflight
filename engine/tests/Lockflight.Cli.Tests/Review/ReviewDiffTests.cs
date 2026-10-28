using System.Security.Cryptography;
using System.Text;
using Lockflight.Cli.Review;

namespace Lockflight.Cli.Tests.Review;

public sealed class ReviewDiffTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-review-diff-");

    [Fact]
    public void ADiffWithinTheLimitIsShownWhole()
    {
        string diff = Section("src/Orders.cs", 10) + Section("src/Prices.cs", 10);

        ReviewDiff fitted = ReviewDiff.Fit(diff, diff.Length, root.FullName);

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

        ReviewDiff fitted = ReviewDiff.Fit(diff, first.Length + last.Length + 50, root.FullName);

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

        ReviewDiff fitted = ReviewDiff.Fit(diff, 200, root.FullName);

        Assert.StartsWith(diff[..200], fitted.Shown, StringComparison.Ordinal);
        Assert.Equal(["src/Orders.cs", "src/Prices.cs"], fitted.NotShown);
    }

    [Fact]
    public void AUnitsFilesAreNamedWithTheUnitPath()
    {
        string diff = Section("src/Orders.cs", 10) + Section("billing/src/Invoices.cs", 500);

        ReviewDiff fitted = ReviewDiff.Fit(diff, 300, root.FullName);

        Assert.Equal(["billing/src/Invoices.cs"], fitted.NotShown);
    }

    [Fact]
    public void AQuotedFileNameIsNamedWithoutItsQuotes()
    {
        string diff = Section("src/Orders.cs", 10) + "diff --git \"a/src/Pr\\303\\251ces.cs\" \"b/src/Pr\\303\\251ces.cs\"\n" + new string('+', 500) + "\n";

        ReviewDiff fitted = ReviewDiff.Fit(diff, 300, root.FullName);

        Assert.Equal(["src/Pr\\303\\251ces.cs"], fitted.NotShown);
    }

    [Theory]
    [InlineData("packages.lock.json")]
    [InlineData("src/Shop/packages.lock.json")]
    [InlineData("web/package-lock.json")]
    [InlineData("web/pnpm-lock.yaml")]
    [InlineData("web/yarn.lock")]
    public void ALockFileIsListedWithItsSizeAndHashAndNotShown(string path)
    {
        string content = "{ \"version\": 1 }\n";
        Write(path, content);
        string orders = Section("src/Orders.cs", 10);
        string diff = orders + Section(path, 50);

        ReviewDiff fitted = ReviewDiff.Fit(diff, orders.Length, root.FullName);

        Assert.True(fitted.IsComplete);
        Assert.Equal(orders.Length, fitted.ReviewedLength);
        Assert.Equal(2, fitted.Files);
        GeneratedFile listed = Assert.Single(fitted.Generated);
        Assert.Equal(new GeneratedFile(path, Encoding.UTF8.GetByteCount(content), Sha256(content)), listed);
        Assert.StartsWith(orders, fitted.Shown, StringComparison.Ordinal);
        Assert.DoesNotContain($"diff --git a/{path}", fitted.Shown, StringComparison.Ordinal);
        Assert.Contains($"- {path} ({listed.Bytes} bytes, sha256 {listed.Sha256})", fitted.Shown, StringComparison.Ordinal);
        Assert.Contains("1 generated file listed, not reviewed", fitted.Shown, StringComparison.Ordinal);
    }

    [Fact]
    public void AModelSnapshotIsListed()
    {
        Write("src/Data/Migrations/ShopContextModelSnapshot.cs", EfSamples.Snapshot);
        string diff = Section("src/Orders.cs", 10) + Section("src/Data/Migrations/ShopContextModelSnapshot.cs", 500);

        ReviewDiff fitted = ReviewDiff.Fit(diff, 1_000, root.FullName);

        Assert.True(fitted.IsComplete);
        Assert.Equal("src/Data/Migrations/ShopContextModelSnapshot.cs", Assert.Single(fitted.Generated).Path);
    }

    [Fact]
    public void AMigrationDesignerIsListedAndItsMigrationShownInFull()
    {
        Write("Migrations/20260101120000_AddOrders.cs", EfSamples.Migration);
        Write("Migrations/20260101120000_AddOrders.Designer.cs", EfSamples.Designer);
        string migration = Section("Migrations/20260101120000_AddOrders.cs", 20);
        string diff = migration + Section("Migrations/20260101120000_AddOrders.Designer.cs", 500);

        ReviewDiff fitted = ReviewDiff.Fit(diff, migration.Length, root.FullName);

        Assert.True(fitted.IsComplete);
        Assert.Equal("Migrations/20260101120000_AddOrders.Designer.cs", Assert.Single(fitted.Generated).Path);
        Assert.StartsWith(migration, fitted.Shown, StringComparison.Ordinal);
    }

    [Fact]
    public void AMigrationDesignerWhoseMigrationIsOnlyInTheTreeIsListed()
    {
        Write("src/Data/Migrations/20260101120000_AddOrders.cs", EfSamples.Migration);
        Write("src/Data/Migrations/20260101120000_AddOrders.Designer.cs", EfSamples.Designer);
        string diff = Section("src/Data/Migrations/20260101120000_AddOrders.Designer.cs", 500);

        ReviewDiff fitted = ReviewDiff.Fit(diff, 100, root.FullName);

        Assert.True(fitted.IsComplete);
        Assert.Single(fitted.Generated);
    }

    [Fact]
    public void ADesignerFileWithoutItsMigrationIsShownInFull()
    {
        Write("Migrations/Orders.Designer.cs", EfSamples.Designer);
        string diff = Section("Migrations/Orders.Designer.cs", 10);

        ReviewDiff fitted = ReviewDiff.Fit(diff, 1_000, root.FullName);

        Assert.Empty(fitted.Generated);
        Assert.Equal(diff, fitted.Shown);
    }

    [Fact]
    public void ADisguisedDesignerFileIsShownInFullAndCountsTowardTheLimit()
    {
        Write("Migrations/20260101120000_AddOrders.cs", EfSamples.Migration);
        Write("Migrations/20260101120000_AddOrders.Designer.cs", EfSamples.Designer.Replace("b.HasKey(\"Id\");", "b.HasKey(\"Id\"); Payments.Refund();", StringComparison.Ordinal));
        string designer = Section("Migrations/20260101120000_AddOrders.Designer.cs", 500);

        ReviewDiff fitted = ReviewDiff.Fit(designer, 100, root.FullName);

        Assert.Empty(fitted.Generated);
        Assert.False(fitted.IsComplete);
        Assert.Equal(["Migrations/20260101120000_AddOrders.Designer.cs"], fitted.NotShown);
    }

    [Fact]
    public void ADisguisedSnapshotIsShownInFull()
    {
        Write("Migrations/ShopContextModelSnapshot.cs", "// <auto-generated />\nnamespace Shop;\npublic static class Payments { public static void Refund() { } }\n");
        string diff = Section("Migrations/ShopContextModelSnapshot.cs", 10);

        ReviewDiff fitted = ReviewDiff.Fit(diff, 1_000, root.FullName);

        Assert.Empty(fitted.Generated);
        Assert.Equal(diff, fitted.Shown);
    }

    [Theory]
    [InlineData("src/Data/ShopContextModelSnapshot.cs")]
    [InlineData("src/OldMigrations/ShopContextModelSnapshot.cs")]
    [InlineData("src/Migrations/Sub/ShopContextModelSnapshot.cs")]
    [InlineData("src/packages.lock.json.bak")]
    [InlineData("src/my-yarn.lock")]
    public void AFileOutsideTheFixedPatternsIsShownInFull(string path)
    {
        Write(path, EfSamples.Snapshot);
        string diff = Section(path, 10);

        Assert.Empty(ReviewDiff.Fit(diff, 1_000, root.FullName).Generated);
    }

    [Fact]
    public void ADeletedLockFileIsShownInFull()
    {
        string diff = Section("packages.lock.json", 10);

        Assert.Empty(ReviewDiff.Fit(diff, 1_000, root.FullName).Generated);
    }

    [Fact]
    public void ALockFileThatIsASymbolicLinkIsShownInFull()
    {
        Assert.SkipUnless(SymbolicLinks.CanCreate, SymbolicLinks.SkipReason);
        Write("outside.txt", "not a lock file\n");
        File.CreateSymbolicLink(Path.Combine(root.FullName, "packages.lock.json"), Path.Combine(root.FullName, "outside.txt"));
        string diff = Section("packages.lock.json", 10);

        Assert.Empty(ReviewDiff.Fit(diff, 1_000, root.FullName).Generated);
    }

    [Fact]
    public void AnAutoGeneratedHeaderOnAnyOtherFileDoesNotListIt()
    {
        Write("src/Orders.Generated.cs", "// <auto-generated />\nnamespace Shop;\n");
        string diff = Section("src/Orders.Generated.cs", 10);

        Assert.Empty(ReviewDiff.Fit(diff, 1_000, root.FullName).Generated);
    }

    [Fact]
    public void GeneratedFilesDoNotCountTowardTheLimit()
    {
        Write("web/package-lock.json", "{}\n");
        string orders = Section("src/Orders.cs", 10);
        string prices = Section("src/Prices.cs", 200);
        string diff = orders + Section("web/package-lock.json", 5_000) + prices;

        ReviewDiff fitted = ReviewDiff.Fit(diff, orders.Length + 50, root.FullName);

        Assert.False(fitted.IsComplete);
        Assert.Equal(["src/Prices.cs"], fitted.NotShown);
        Assert.Equal(orders.Length + prices.Length, fitted.ReviewedLength);
        Assert.Equal(diff.Length, fitted.Length);
        Assert.Contains("generated file listed, not reviewed", fitted.Shown, StringComparison.Ordinal);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private void Write(string path, string content)
    {
        string file = Path.Combine(root.FullName, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
    }

    private static string Sha256(string content) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private static string Section(string path, int lines) =>
        $"diff --git a/{path} b/{path}\n--- a/{path}\n+++ b/{path}\n@@ -1 +1,{lines} @@\n" + string.Concat(Enumerable.Repeat("+line\n", lines));
}
