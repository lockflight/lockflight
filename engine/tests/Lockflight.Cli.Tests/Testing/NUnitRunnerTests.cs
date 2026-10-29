using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Testing;

public sealed class NUnitRunnerTests
{
    [Fact]
    public void MethodsAreSelectedByClassAndMethod() =>
        Assert.Equal(
            "(class == \"Shop.Tests.Prices\" && method == \"Costs\") || (class == \"Shop.Tests.Outer+Inner\" && method == \"Works\")",
            NUnitRunner.Where(["Shop.Tests.Prices.Costs", "Shop.Tests.Outer+Inner.Works"]));

    [Fact]
    public void TheSelectionIsEscapedInsideTheRunSettings() =>
        Assert.Equal(
            "<RunSettings>\n  <NUnit>\n    <Where>(class == \"A.B\" &amp;&amp; method == \"C\")</Where>\n  </NUnit>\n</RunSettings>",
            NUnitRunner.RunSettings(["A.B.C"]).ReplaceLineEndings("\n"));
}
