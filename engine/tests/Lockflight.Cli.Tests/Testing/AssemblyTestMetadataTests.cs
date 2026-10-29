using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Testing;

public sealed class AssemblyTestMetadataTests
{
    private const string Samples = "Lockflight.Cli.Tests.Testing.FrameworkSamples.";

    private static readonly string Assembly = typeof(AssemblyTestMetadataTests).Assembly.Location;

    [Fact]
    public void NUnitTestsAreTheMethodsWithATestAttribute() =>
        Assert.Equal(
            ["NUnitFixture.Categorised", "NUnitFixture.ExplicitOnly", "NUnitFixture.FromSource", "NUnitFixture.Ignored", "NUnitFixture.Plain", "NUnitFixture.Rows", "NUnitFixture.Theory"],
            Methods(TestFrameworkKind.NUnit, "NUnitFixture."));

    [Fact]
    public void NUnitRunsNestedInternalStaticAndInheritedFixturesButNoOpenGenericOne() =>
        Assert.Equal(
            ["NUnitClosedGeneric.FromGenericBase", "NUnitDerived.Inherited", "NUnitFixture+Nested.Inner", "NUnitInternalFixture.Marked", "NUnitStaticFixture.Static"],
            Methods(TestFrameworkKind.NUnit).Where(name => !name.StartsWith("NUnitFixture.", StringComparison.Ordinal)));

    [Fact]
    public void NUnitPropertiesNamedRequirementAreRequirementsAndCategoriesAreNot()
    {
        IReadOnlyList<TestCase> tests = Tests(TestFrameworkKind.NUnit);

        Assert.Equal(["FR-001"], Single(tests, "NUnitFixture.Plain").Requirements);
        Assert.Empty(Single(tests, "NUnitFixture.Categorised").Requirements);
        Assert.Equal(["FR-003", "FR-004"], Single(tests, "NUnitInternalFixture.Marked").Requirements);
    }

    [Fact]
    public void NUnitIgnoreAndExplicitOnAMethodOrItsClassAreRead()
    {
        IReadOnlyList<TestCase> tests = Tests(TestFrameworkKind.NUnit);

        Assert.Equal(("later", false), (Single(tests, "NUnitFixture.Ignored").SkipReason, Single(tests, "NUnitFixture.Ignored").IsExplicit));
        Assert.Equal((null, true), (Single(tests, "NUnitFixture.ExplicitOnly").SkipReason, Single(tests, "NUnitFixture.ExplicitOnly").IsExplicit));
        Assert.Equal(("whole class", true), (Single(tests, "NUnitInternalFixture.Marked").SkipReason, Single(tests, "NUnitInternalFixture.Marked").IsExplicit));
    }

    [Fact]
    public void EachFrameworkReadsOnlyItsOwnAttributes()
    {
        // xUnit v2's attributes match by name in any namespace (custom ones derive from them), so NUnit's [Theory] reads as one.
        Assert.Equal(["NUnitFixture.Theory"], Methods(TestFrameworkKind.XunitV2));
    }

    private static IReadOnlyList<TestCase> Tests(TestFrameworkKind framework) =>
        [.. AssemblyTestMetadata.Read(Assembly, framework).Where(test => test.Class.StartsWith(Samples, StringComparison.Ordinal))];

    private static string[] Methods(TestFrameworkKind framework, string prefix = "") =>
        [.. Tests(framework).Select(test => $"{test.Class[Samples.Length..]}.{test.Method}").Where(name => name.StartsWith(prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal)];

    private static TestCase Single(IReadOnlyList<TestCase> tests, string name) => Assert.Single(tests, test => $"{test.Class[Samples.Length..]}.{test.Method}" == name);
}
