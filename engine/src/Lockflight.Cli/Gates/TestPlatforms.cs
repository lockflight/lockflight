namespace Lockflight.Cli.Gates;

/// <summary>
/// The test platforms the gates drive, one row each. On VSTest the collectors are data collectors (<c>--collect</c>):
/// coverlet.collector, or Microsoft's collector that Microsoft.NET.Test.Sdk brings; on Microsoft.Testing.Platform they are
/// extensions with their own options.
/// </summary>
internal static class TestPlatforms
{
    public static IReadOnlyList<TestPlatform> All { get; } =
    [
        new(TestRunnerKind.MicrosoftTestingPlatform,
            ["--coverage", "--coverage-output-format", "cobertura"], "Microsoft.Testing.Extensions.CodeCoverage",
            ["--coverlet", "--coverlet-output-format", "cobertura"], "coverlet.MTP"),
        new(TestRunnerKind.VSTest,
            ["--collect", "Code Coverage;Format=cobertura"], "Microsoft.NET.Test.Sdk",
            ["--collect", "XPlat Code Coverage"], "coverlet.collector"),
    ];

    public static TestPlatform For(TestRunnerKind kind) => All.Single(platform => platform.Kind == kind);
}
