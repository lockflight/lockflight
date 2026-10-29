using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Testing;

public sealed class TraitConditionTests
{
    [Theory]
    [InlineData("Category!=Integration", "Category", "Integration", true)]
    [InlineData(" TestCategory = Fast ", "TestCategory", "Fast", false)]
    public void OneTraitConditionIsParsed(string filter, string name, string value, bool negated) =>
        Assert.Equal(new TraitCondition(name, value, negated), TraitCondition.Parse(filter));

    [Theory]
    [InlineData("Category!=Integration&Category!=Slow")]
    [InlineData("Category=Fast|Priority=1")]
    [InlineData("FullyQualifiedName~Shop.Tests")]
    [InlineData("(Category!=Integration)")]
    public void AnythingElseIsNoTraitCondition(string filter) => Assert.Null(TraitCondition.Parse(filter));

    [Theory]
    [InlineData("Category!=Integration", "Category", "integration", true)]
    [InlineData("Category!=Integration", "Category", "Fast", false)]
    [InlineData("Category!=Integration", null, null, false)]
    [InlineData("Category=Fast", "category", "FAST", false)]
    [InlineData("Category=Fast", "Category", "Slow", true)]
    [InlineData("Category=Fast", null, null, true)]
    public void ATestIsExcludedByItsTraitsWithoutRegardToCase(string filter, string? name, string? value, bool excluded)
    {
        TestCase test = new("Shop.Tests", "Shop.Tests.A", "B", [], null) { Traits = name is null ? [] : [KeyValuePair.Create(name, value!)] };

        Assert.Equal(excluded, TraitCondition.Parse(filter)!.Excludes(test));
    }

    [Fact]
    public void EachRunnerTakesTheConditionInItsOwnOptions()
    {
        TraitCondition integration = TraitCondition.Parse("Category!=Integration")!;
        TraitCondition fast = TraitCondition.Parse("Category=Fast")!;

        Assert.Equal(["-trait-", "Category=Integration"], NativeTestRunner.TraitArguments(RunnerMode.Native, integration));
        Assert.Equal(["-trait", "Category=Fast"], NativeTestRunner.TraitArguments(RunnerMode.Native, fast));
        Assert.Equal(["--filter-not-trait", "Category=Integration"], NativeTestRunner.TraitArguments(RunnerMode.Mtp, integration));
        Assert.Equal(["--filter-trait", "Category=Fast"], NativeTestRunner.TraitArguments(RunnerMode.Mtp, fast));
        Assert.Equal(["--filter", "Category!=Integration"], VSTestRunner.SuiteArguments(integration));
        Assert.Equal(["--filter", "Category=a\\,b"], VSTestRunner.SuiteArguments(new TraitCondition("Category", "a,b", Negated: false)));
    }

    [Fact]
    public void AWholeSuiteRunTakesTheConditionAndAMethodRunDoesNot()
    {
        TraitCondition integration = TraitCondition.Parse("Category!=Integration")!;

        Assert.Equal(["--filter", "Category!=Integration"], Assert.Single(VSTestRunner.FilterBatches(null, integration)));
        Assert.Empty(Assert.Single(VSTestRunner.FilterBatches(null, null)));
        Assert.Equal(["--filter", "FullyQualifiedName=A.B"], Assert.Single(VSTestRunner.FilterBatches(["A.B"], integration)));
    }

    [Theory]
    [InlineData(1, "test.stopFilter 'Category!=Integration' left out 1 test (the pr gate and CI run them)")]
    [InlineData(3, "test.stopFilter 'Category!=Integration' left out 3 tests (the pr gate and CI run them)")]
    public void TheRegressionFilterSaysWhatItLeftOut(int excluded, string expected) =>
        Assert.Equal(expected, new RegressionFilter(["Category!=Integration"], excluded).Describe());
}
