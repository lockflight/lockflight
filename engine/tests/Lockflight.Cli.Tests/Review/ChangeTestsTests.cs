using Lockflight.Cli.Review;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Review;

public sealed class ChangeTestsTests : IDisposable
{
    private const string Tests = """
        namespace Shop.Tests;

        public sealed class PriceTests
        {
            [Fact]
            public void Unchanged() => Assert.True(true);

            [Fact]
            public void Changed()
            {
                Assert.Equal(1, 1);
            }

            [Theory]
            [InlineData(1)]
            public void Added(int value) => Assert.Equal(1, value);

            private static int Helper() => 2;
        }
        """;

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-own-tests-");

    [Fact]
    public void TheTestsWhoseLinesTheDiffAddsOrChangesAreTheChangesOwnTests()
    {
        string file = Path.Combine(root.FullName, "tests", "Shop.Tests", "PriceTests.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, Tests.ReplaceLineEndings("\n"));
        TestApp app = new(Path.Combine(root.FullName, "tests", "Shop.Tests"), Path.Combine(root.FullName, "tests", "Shop.Tests", "bin", "Shop.Tests.dll"));
        TestCase[] listed =
        [
            Listed("Shop.Tests.PriceTests", "Unchanged"),
            Listed("Shop.Tests.PriceTests", "Changed"),
            Listed("Shop.Tests.PriceTests", "Added"),
            Listed("Shop.Tests.OtherTests", "Changed"),
        ];
        const string diff = """
            diff --git a/tests/Shop.Tests/PriceTests.cs b/tests/Shop.Tests/PriceTests.cs
            --- a/tests/Shop.Tests/PriceTests.cs
            +++ b/tests/Shop.Tests/PriceTests.cs
            @@ -9,10 +9,14 @@ public sealed class PriceTests
                 public void Changed()
                 {
            -        Assert.Equal(2, 2);
            +        Assert.Equal(1, 1);
                 }

            +    [Theory]
            +    [InlineData(1)]
            +    public void Added(int value) => Assert.Equal(1, value);
            +
                 private static int Helper() => 2;
             }
            diff --git a/src/Shop/PriceCalculator.cs b/src/Shop/PriceCalculator.cs
            --- a/src/Shop/PriceCalculator.cs
            +++ b/src/Shop/PriceCalculator.cs
            @@ -1 +1 @@
            -namespace Shop;
            +namespace Shop.Pricing;
            """;

        IReadOnlyList<TestCase> own = ChangeTests.Find(diff, root.FullName, [app], listed);

        Assert.Equal(["Shop.Tests.PriceTests.Added", "Shop.Tests.PriceTests.Changed"], own.Select(test => test.MethodFilter).Order(StringComparer.Ordinal));
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private static TestCase Listed(string className, string method) => new("Shop.Tests", className, method, [], null);
}
