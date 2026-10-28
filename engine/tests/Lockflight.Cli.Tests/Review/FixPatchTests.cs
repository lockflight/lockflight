using Lockflight.Cli.Review;

namespace Lockflight.Cli.Tests.Review;

public sealed class FixPatchTests : IDisposable
{
    private const string Library = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>";
    private const string TestProject = "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"xunit.v3\" Version=\"3.2.0\" /></ItemGroup></Project>";
    private const string Calculator = "namespace Shop;\n\npublic static class PriceCalculator\n{\n    public static decimal Total(decimal price, decimal discount) => price;\n}\n";

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-fix-");

    public FixPatchTests()
    {
        Write("src/Shop/Shop.csproj", Library);
        Write("src/Shop/PriceCalculator.cs", Calculator);
        Write("tests/Shop.Tests/Shop.Tests.csproj", TestProject);
        Write("tests/Shop.Tests/PriceTests.cs", "namespace Shop.Tests;\n\npublic sealed class PriceTests\n{\n}\n");
    }

    [Fact]
    public void AHunkIsAppliedWhereItsLinesMatchWhateverItsHeaderCounts()
    {
        FixPatchResult result = FixPatch.Prepare(
            """
            --- a/src/Shop/PriceCalculator.cs
            +++ b/src/Shop/PriceCalculator.cs
            @@ -40,9 +40,9 @@
             {
            -    public static decimal Total(decimal price, decimal discount) => price;
            +    public static decimal Total(decimal price, decimal discount) => price - discount;
             }
            """,
            root.FullName);

        Assert.Null(result.Error);
        FixedFile patched = Assert.Single(result.Files);
        Assert.Equal(Full("src/Shop/PriceCalculator.cs"), patched.Path);
        Assert.Equal(Full("src/Shop/Shop.csproj"), patched.Project);
        Assert.Equal(Calculator.Replace("=> price;", "=> price - discount;", StringComparison.Ordinal), patched.Text);
        Assert.Equal(Calculator, File.ReadAllText(Full("src/Shop/PriceCalculator.cs")));
    }

    [Fact]
    public void AHunkWhoseLinesAreNotInTheFileDoesNotApply()
    {
        FixPatchResult result = FixPatch.Prepare(
            "--- a/src/Shop/PriceCalculator.cs\n+++ b/src/Shop/PriceCalculator.cs\n@@ -5 +5 @@\n-    public static decimal Total(decimal price) => price;\n+    public static decimal Total(decimal price) => 0m;\n",
            root.FullName);

        Assert.Empty(result.Files);
        Assert.Contains("src/Shop/PriceCalculator.cs", result.Error, StringComparison.Ordinal);
        Assert.Contains("hunk 1", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void AFixToTestCodeIsRefused()
    {
        FixPatchResult result = FixPatch.Prepare(
            "--- a/tests/Shop.Tests/PriceTests.cs\n+++ b/tests/Shop.Tests/PriceTests.cs\n@@ -3 +3 @@\n-public sealed class PriceTests\n+public sealed class OtherTests\n",
            root.FullName);

        Assert.Contains("test project", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--- /dev/null\n+++ b/src/Shop/New.cs\n@@ -0,0 +1 @@\n+namespace Shop;\n")]
    [InlineData("--- a/src/Shop/PriceCalculator.cs\n+++ /dev/null\n@@ -1 +0,0 @@\n-namespace Shop;\n")]
    [InlineData("--- a/../outside/PriceCalculator.cs\n+++ b/../outside/PriceCalculator.cs\n@@ -1 +1 @@\n-namespace Shop;\n+namespace Other;\n")]
    [InlineData("--- a/src/Shop/Shop.csproj\n+++ b/src/Shop/Shop.csproj\n@@ -1 +1 @@\n-x\n+y\n")]
    public void AFixMayOnlyChangeExistingCSharpFilesOfTheRepository(string patch) =>
        Assert.NotNull(FixPatch.Prepare(patch, root.FullName).Error);

    [Fact]
    public void ADiffWithoutAFileIsRefused() =>
        Assert.Contains("no file", FixPatch.Prepare("make Total subtract the discount", root.FullName).Error, StringComparison.Ordinal);

    [Fact]
    public void AFileWithWindowsLineEndingsKeepsThem()
    {
        Write("src/Shop/PriceCalculator.cs", Calculator.ReplaceLineEndings("\r\n"));

        FixPatchResult result = FixPatch.Prepare(
            "diff --git a/src/Shop/PriceCalculator.cs b/src/Shop/PriceCalculator.cs\n--- a/src/Shop/PriceCalculator.cs\n+++ b/src/Shop/PriceCalculator.cs\n@@ -5 +5 @@\n-    public static decimal Total(decimal price, decimal discount) => price;\n+    public static decimal Total(decimal price, decimal discount) => price - discount;\n",
            root.FullName);

        Assert.Equal(Calculator.Replace("=> price;", "=> price - discount;", StringComparison.Ordinal).ReplaceLineEndings("\r\n"), Assert.Single(result.Files).Text);
    }

    [Fact]
    public void AnEmptyContextLineWithoutItsLeadingSpaceStillMatches()
    {
        FixPatchResult result = FixPatch.Prepare(
            "--- a/src/Shop/PriceCalculator.cs\n+++ b/src/Shop/PriceCalculator.cs\n@@ -1,3 +1,3 @@\n namespace Shop;\n\n-public static class PriceCalculator\n+internal static class PriceCalculator\n",
            root.FullName);

        Assert.StartsWith("namespace Shop;\n\ninternal static class", Assert.Single(result.Files).Text, StringComparison.Ordinal);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private string Full(string path) => Path.Combine(root.FullName, path.Replace('/', Path.DirectorySeparatorChar));

    private void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Full(path))!);
        File.WriteAllText(Full(path), content);
    }
}
