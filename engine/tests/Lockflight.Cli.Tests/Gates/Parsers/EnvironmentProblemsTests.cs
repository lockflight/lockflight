using Lockflight.Cli.Gates.Parsers;

namespace Lockflight.Cli.Tests.Gates.Parsers;

public sealed class EnvironmentProblemsTests
{
    private const string MissingRuntimeOutput = """
        Exit code: 150
          Error output: You must install or update .NET to run this application.
          Framework: 'Microsoft.NETCore.App', version '8.0.0' (arm64)
          The following frameworks were found:
        """;

    [Fact]
    public void AMissingRuntimeNamesTheVersionAndTheFrameworkSetting()
    {
        EnvironmentProblem? problem = EnvironmentProblems.Recognize(MissingRuntimeOutput);

        Assert.NotNull(problem);
        Assert.Equal("missing-runtime", problem.Code);
        Assert.Contains(".NET 8.0.0 runtime", problem.Explanation, StringComparison.Ordinal);
        Assert.Contains("gates.test.framework", problem.Explanation, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("error : Platform linker ('clang' or 'gcc') not found in PATH.", "missing-native-toolchain")]
    [InlineData("error NU1301: Unable to load the service index for source https://api.nuget.org/v3/index.json.", "restore-offline")]
    [InlineData("Docker is either not running or misconfigured.", "docker-unavailable")]
    public void KnownMachineFailuresAreRecognised(string output, string expectedCode) =>
        Assert.Equal(expectedCode, EnvironmentProblems.Recognize(output)?.Code);

    [Fact]
    public void AnOrdinaryTestFailureIsNotAnEnvironmentProblem() =>
        Assert.Null(EnvironmentProblems.Recognize("failed Shop.Tests.PriceCalculatorTests.LineTotal (12ms)\n  Assert.Equal() Failure"));
}
