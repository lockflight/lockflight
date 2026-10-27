using Lockflight.Cli.Review;

namespace Lockflight.Cli.Tests.Review;

public sealed class CompileErrorsTests
{
    [Fact]
    public void ErrorsAreReadOnceWithoutTheirDirectories()
    {
        const string output = """
            /tmp/lfr-1/Finding2.cs(4,13): error CS0117: 'Pricing' does not contain a definition for 'Totl' [/repo/tests/Shop.Tests/Shop.Tests.csproj]
            C:\temp\lfr-1\Finding2.cs(4,13): warning CS8600: Converting null literal [C:\repo\Shop.Tests.csproj]
            Build FAILED.
            /tmp/lfr-1/Finding2.cs(4,13): error CS0117: 'Pricing' does not contain a definition for 'Totl' [/repo/tests/Shop.Tests/Shop.Tests.csproj]
            C:\temp\lfr-1\LockflightReproductions.g.cs(3,7): error CS0246: The type or namespace name 'Moq' could not be found [C:\repo\Shop.Tests.csproj]
            /usr/share/dotnet/sdk/NuGet.targets(1,1): error NU1101: Unable to find package Nope.
            error MSB1009: Project file does not exist.
            """;

        IReadOnlyList<CompileError> errors = CompileErrors.Parse(output);

        Assert.Equal(
            [
                "Finding2.cs(4,13): CS0117: 'Pricing' does not contain a definition for 'Totl'",
                "LockflightReproductions.g.cs(3,7): CS0246: The type or namespace name 'Moq' could not be found",
                "NuGet.targets(1,1): NU1101: Unable to find package Nope.",
                "MSB1009: Project file does not exist.",
            ],
            errors.Select(error => error.ToString()));
    }

    [Fact]
    public void TheSummaryIsBounded()
    {
        CompileError[] errors = [.. Enumerable.Range(1, 30).Select(line => new CompileError("Finding1.cs", line, 1, "CS0103", new string('x', 500)))];

        string summary = CompileErrors.Summary(errors);

        Assert.True(summary.Length <= CompileErrors.MaxSummaryCharacters + 40, summary);
        Assert.Contains("Finding1.cs(1,1): CS0103:", summary, StringComparison.Ordinal);
        Assert.Contains("… and 20 more", summary, StringComparison.Ordinal);
    }
}
