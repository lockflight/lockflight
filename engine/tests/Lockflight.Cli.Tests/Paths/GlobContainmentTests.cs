using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Tests.Paths;

/// <summary>
/// A glob covers another only when it matches every path the other matches, under the protected-path matcher's own semantics;
/// when unsure, it does not.
/// </summary>
public sealed class GlobContainmentTests
{
    private static readonly string[] Globs =
    [
        "**", "*", "src/**", "src/*", "src/Payments/**", "src/Payments/*.cs", "src/*/Db/**", "src/**/Db/**", "**/Db/**", "**/*.cs",
        "src/**/*.cs", "src/Pay*/**", "src/Pay*s/**", "src/P?y*/**", "deploy/**", "deploy", "Src/**", "src/**/**", "**/*", "*.cs",
        "src/Payments", "src/[Pp]ayments/**", "./src/**", "src//Payments/**", "src/**.cs",
    ];

    private static readonly string[] Paths =
    [
        "src", "src/", "src/a.cs", "src/Payments", "src/Payments/a.cs", "src/Payments/b.txt", "src/Payments/Db/x.cs", "src/Orders/Db/x.cs",
        "src/x/y/Db/z.sql", "Db/a.sql", "a.cs", "deploy", "deploy/main.bicep", "Src/Payments/a.cs", "src/Pays/a.cs", "src/Plays/a",
        "src/Payment/a.cs", "tests/a.cs", "src/Payments/Inner/c.cs", "x/Db/y", "src/Pay/a",
    ];

    [Theory]
    [InlineData("src/**", "src/Payments/**", true)]
    [InlineData("src/**", "src/Payments/*.cs", true)]
    [InlineData("**", "deploy/**", true)]
    [InlineData("**/*.cs", "src/**/*.cs", true)]
    [InlineData("**/Db/**", "src/*/Db/**", true)]
    [InlineData("src/**/Db/**", "src/*/Db/**", true)]
    [InlineData("src/Pay*/**", "src/Pay*s/**", true)]
    [InlineData("src/*", "src/Payments", true)]
    [InlineData("src/Payments/**", "src/**", false)]
    [InlineData("src/*/Db/**", "src/**/Db/**", false)]
    [InlineData("src/**", "src", false)]
    [InlineData("deploy/**", "deploy", false)]
    [InlineData("src/Pay*s/**", "src/Pay*/**", false)]
    [InlineData("Src/**", "src/Payments/**", true)]
    [InlineData("src/**", "./src/**", false)]
    [InlineData("src/[Pp]ayments/**", "src/Payments/**", false)]
    [InlineData("src/[Pp]ayments/**", "src/[Pp]ayments/**", true)]
    public void AGlobCoversAnotherOnlyWhenItMatchesEveryPathTheOtherDoes(string outer, string inner, bool expected) =>
        Assert.Equal(expected, GlobContainment.Covers(outer, inner));

    [Fact]
    public void ACoveringGlobMatchesEverySamplePathTheCoveredOneMatches()
    {
        foreach (string outer in Globs)
        {
            foreach (string inner in Globs.Where(inner => GlobContainment.Covers(outer, inner)))
            {
                string? missed = Paths.FirstOrDefault(path => PathRules.Matches(inner, path) && !PathRules.Matches(outer, path));
                Assert.True(missed is null, $"{outer} covers {inner} but misses {missed}");
            }
        }
    }
}
