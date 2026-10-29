using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Gates.Steps;

namespace Lockflight.Cli.Tests.Gates;

public sealed class TestPlatformsTests
{
    [Theory]
    [InlineData(true, "microsoft", "--coverage --coverage-output-format cobertura", "Microsoft.Testing.Extensions.CodeCoverage")]
    [InlineData(true, "coverlet", "--coverlet --coverlet-output-format cobertura", "coverlet.MTP")]
    [InlineData(true, "none", "", null)]
    [InlineData(false, "microsoft", "--collect|Code Coverage;Format=cobertura", "Microsoft.NET.Test.Sdk")]
    [InlineData(false, "coverlet", "--collect|XPlat Code Coverage", "coverlet.collector")]
    [InlineData(false, "none", "", null)]
    public void EachPlatformNamesTheArgumentsAndPackageOfEachCollector(bool testingPlatform, string collectorName, string arguments, string? package)
    {
        CoverageCollector collector = collectorName switch { "microsoft" => CoverageCollector.Microsoft, "coverlet" => CoverageCollector.Coverlet, _ => CoverageCollector.None };
        TestRunnerKind runner = testingPlatform ? TestRunnerKind.MicrosoftTestingPlatform : TestRunnerKind.VSTest;
        string separator = testingPlatform ? " " : "|";

        Assert.Equal(arguments, string.Join(separator, CoverageStep.TestArguments(collector, runner)));
        Assert.Equal(package, CoverageStep.PackageOf(collector, runner));
    }

    [Fact]
    public void EveryTestPlatformHasOneRow() =>
        Assert.Equal(Enum.GetValues<TestRunnerKind>().Order(), TestPlatforms.All.Select(platform => platform.Kind).Order());
}
