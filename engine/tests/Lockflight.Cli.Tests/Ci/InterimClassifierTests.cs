using Lockflight.Cli.Ci;
using Lockflight.Cli.Config;
using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Tests.Ci;

public sealed class InterimClassifierTests
{
    private static readonly PolicySource Policy = new("main", new string('a', 40), ManifestBlob: null, "interim");

    [Fact]
    public void OnlyLowRiskPathsAreAFix()
    {
        InterimClassification result = Classify(["docs/guide.md", "README.md", ".github/ISSUE_TEMPLATE/bug.md"]);

        Assert.Equal(ChangeClass.Fix, result.Classification.Class);
        Assert.Equal(InterimShape.LowRisk, result.Shape);
        Assert.Equal(SignalStrength.Boundary, Assert.Single(result.Classification.Signals).Strength);
    }

    [Theory]
    [InlineData("src/Shop/Price.cs")]
    [InlineData("tests/Shop.Tests/PriceTests.cs")]
    [InlineData("Directory.Build.props")]
    [InlineData("src/docs/notes.md")]
    [InlineData("specs/001-x/spec.md")]
    public void AnyOtherPathIsABehaviourChange(string path)
    {
        InterimClassification result = Classify(["docs/guide.md", path]);

        Assert.Equal(ChangeClass.Behaviour, result.Classification.Class);
        Assert.Equal(InterimShape.Other, result.Shape);
        Assert.Contains(path, result.Classification.Signals[0].Where, StringComparison.Ordinal);
    }

    [Fact]
    public void PackageVersionLinesAndLockFilesOnlyAreDependencyOnly()
    {
        string diff = """
            diff --git a/Directory.Packages.props b/Directory.Packages.props
            --- a/Directory.Packages.props
            +++ b/Directory.Packages.props
            @@ -3 +3 @@
            -    <PackageVersion Include="xunit.v3" Version="3.0.0" />
            +    <PackageVersion Include="xunit.v3" Version="3.1.0" />
            """;

        InterimClassification result = Classify(["Directory.Packages.props", "src/Shop/packages.lock.json"], diff);

        Assert.Equal(ChangeClass.Behaviour, result.Classification.Class);
        Assert.Equal(InterimShape.DependencyOnly, result.Shape);
    }

    [Fact]
    public void AnyOtherLineInAPackageFileIsNotDependencyOnly()
    {
        string diff = """
            diff --git a/src/Shop/Shop.csproj b/src/Shop/Shop.csproj
            --- a/src/Shop/Shop.csproj
            +++ b/src/Shop/Shop.csproj
            @@ -3 +3,2 @@
            -    <PackageReference Include="Serilog" Version="3.0.0" />
            +    <PackageReference Include="Serilog" Version="4.0.0" />
            +    <Compile Include="..\Shared\**\*.cs" />
            """;

        Assert.Equal(InterimShape.Other, Classify(["src/Shop/Shop.csproj"], diff).Shape);
    }

    [Fact]
    public void ASwappedPackageIsNotAVersionBump()
    {
        string diff = """
            diff --git a/src/Shop/Shop.csproj b/src/Shop/Shop.csproj
            --- a/src/Shop/Shop.csproj
            +++ b/src/Shop/Shop.csproj
            @@ -3 +3 @@
            -    <PackageReference Include="FluentAssertions" Version="6.0.0" />
            +    <PackageReference Include="Shouldly" Version="4.0.0" />
            """;

        Assert.Equal(InterimShape.Other, Classify(["src/Shop/Shop.csproj"], diff).Shape);
    }

    [Fact]
    public void AnAddedProjectFileIsNotDependencyOnly() =>
        Assert.Equal(InterimShape.Other, InterimClassifier.Classify(["src/New/New.csproj"], CiSettings.DefaultLowRiskPaths, new HashSet<string>(), new HashSet<string> { "src/New/New.csproj" }, string.Empty, Policy).Shape);

    [Fact]
    public void ABaseAllowlistCanWidenTheLowRiskPaths() =>
        Assert.Equal(ChangeClass.Fix, InterimClassifier.Classify(["site/index.html"], ["site/**"], new HashSet<string>(), new HashSet<string>(), string.Empty, Policy).Classification.Class);

    private static InterimClassification Classify(IReadOnlyList<string> changed, string diff = "")
    {
        HashSet<string> both = [.. changed];
        return InterimClassifier.Classify(changed, CiSettings.DefaultLowRiskPaths, both, both, diff, Policy);
    }
}
