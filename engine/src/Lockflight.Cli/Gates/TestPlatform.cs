using Lockflight.Cli.Config;

namespace Lockflight.Cli.Gates;

/// <summary>
/// What the gates need to know about one test platform <c>dotnet test</c> drives (<see cref="TestRunnerKind"/>): for each coverage
/// collector, the test arguments that make it write Cobertura and the NuGet package every test project must reference for it.
/// </summary>
internal sealed record TestPlatform(
    TestRunnerKind Kind,
    IReadOnlyList<string> MicrosoftCoverageArguments,
    string MicrosoftCoveragePackage,
    IReadOnlyList<string> CoverletArguments,
    string CoverletPackage)
{
    /// <summary>The test arguments for the collector; empty when coverage is not configured.</summary>
    public IReadOnlyList<string> CoverageArguments(CoverageCollector collector) => collector switch
    {
        CoverageCollector.Microsoft => MicrosoftCoverageArguments,
        CoverageCollector.Coverlet => CoverletArguments,
        _ => [],
    };

    /// <summary>The package the collector needs in every test project; null for none.</summary>
    public string? CoveragePackage(CoverageCollector collector) => collector switch
    {
        CoverageCollector.Microsoft => MicrosoftCoveragePackage,
        CoverageCollector.Coverlet => CoverletPackage,
        _ => null,
    };
}
