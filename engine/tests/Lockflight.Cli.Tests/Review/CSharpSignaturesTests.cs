using Lockflight.Cli.Review;

namespace Lockflight.Cli.Tests.Review;

public sealed class CSharpSignaturesTests
{
    private const string Source = """
        using System.Text;

        namespace Shop.Orders;

        /// <summary>Totals { with a brace in a comment.</summary>
        public sealed class OrderTotals(IClock clock)
        {
            private const string Secret = "{ not a block";
            public const int Limit = 10;

            private readonly Dictionary<string, int> counts = new() { ["a"] = 1 };

            public OrderTotals() : this(new SystemClock())
            {
                string text = $"{Limit} }} {{";
            }

            public int Count { get; private set; } = 3;

            public decimal Total(decimal price, decimal discount = 0m)
            {
                if (price > 0) { return price - discount; }
                return 0;
            }

            [Obsolete("old")]
            internal static bool IsLarge(decimal total) => total > Limit;

            private void Hidden() { }

            public enum Kind { Retail = 1, Wholesale = 2 }

            private sealed class Helper { public void Inside() { } }
        }

        public interface IClock
        {
            DateTimeOffset Now { get; }
        }

        internal sealed record Line(string Sku, int Quantity);
        """;

    [Fact]
    public void TypesAndReachableMembersAreListedWithoutBodies()
    {
        string listing = CSharpSignatures.Extract(Source, 10_000);

        Assert.Equal(
            """
            namespace Shop.Orders;
            public sealed class OrderTotals(IClock clock) {
              public const int Limit = …;
              public OrderTotals() : this(new SystemClock()) { … }
              public int Count { … }
              public decimal Total(decimal price, decimal discount = 0m) { … }
              internal static bool IsLarge(decimal total) => …;
              public enum Kind { Retail = 1, Wholesale = 2 }
            }
            public interface IClock {
              DateTimeOffset Now { … }
            }
            internal sealed record Line(string Sku, int Quantity);
            """.ReplaceLineEndings("\n"),
            listing);
    }

    [Fact]
    public void ARawStringWithBracesDoesNotEndTheType()
    {
        string source = "namespace A;\n\npublic static class Prompt\n{\n    public const string Text = \"\"\"\n        { \"a\": } }\n        \"\"\";\n\n    public static string Build() => Text;\n}\n";

        string listing = CSharpSignatures.Extract(source, 10_000);

        Assert.Contains("public static string Build() => …;", listing, StringComparison.Ordinal);
        Assert.EndsWith("}", listing, StringComparison.Ordinal);
    }

    [Fact]
    public void BlockNamespacesNestTheirTypes()
    {
        string listing = CSharpSignatures.Extract("namespace A.B\n{\n    class Plain { public void Run() { } }\n}\n", 10_000);

        Assert.Equal("namespace A.B {\n  class Plain {\n    public void Run() { … }\n  }\n}", listing);
    }

    [Fact]
    public void TheListingStopsAtTheLimit()
    {
        string source = "namespace A;\npublic class Big\n{\n" + string.Concat(Enumerable.Range(0, 200).Select(index => $"    public int Value{index}() => {index};\n")) + "}\n";

        string listing = CSharpSignatures.Extract(source, 500);

        Assert.True(listing.Length < 600, listing);
        Assert.EndsWith("// … (truncated)", listing, StringComparison.Ordinal);
    }
}
