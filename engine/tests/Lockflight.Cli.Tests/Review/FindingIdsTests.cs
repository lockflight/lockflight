using Lockflight.Cli.Review;

namespace Lockflight.Cli.Tests.Review;

public sealed class FindingIdsTests
{
    [Fact]
    public void TheSameClaimAtTheSamePlaceHasTheSameId() =>
        Assert.Equal(
            FindingIds.Of(ReviewNames.GeneralDimension, "src/Orders.cs", 12, "Off by one in the discount"),
            FindingIds.Of(ReviewNames.GeneralDimension, "src/Orders.cs", 12, "Off by one in the discount"));

    [Fact]
    public void CaseSpacingAndPathSeparatorsDoNotChangeTheId() =>
        Assert.Equal(
            FindingIds.Of(ReviewNames.GeneralDimension, "src/Orders.cs", 12, "Off by one in the discount"),
            FindingIds.Of(ReviewNames.GeneralDimension, "src\\Orders.cs", 12, "  off by ONE   in the discount "));

    [Theory]
    [InlineData("tests", "src/Orders.cs", 12, "Off by one in the discount")]
    [InlineData("general", "src/Billing.cs", 12, "Off by one in the discount")]
    [InlineData("general", "src/Orders.cs", 13, "Off by one in the discount")]
    [InlineData("general", "src/Orders.cs", 12, "Null customer")]
    public void ADifferentDimensionFileLineOrClaimHasADifferentId(string dimension, string file, int line, string title) =>
        Assert.NotEqual(FindingIds.Of(ReviewNames.GeneralDimension, "src/Orders.cs", 12, "Off by one in the discount"), FindingIds.Of(dimension, file, line, title));

    [Fact]
    public void AnIdIsTwelveLowercaseHexCharacters() =>
        Assert.Matches("^[0-9a-f]{12}$", FindingIds.Of(ReviewNames.GeneralDimension, "a.cs", 1, "x"));
}
