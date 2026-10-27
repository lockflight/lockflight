using Lockflight.Cli.Adoption;

namespace Lockflight.Cli.Tests.Adoption;

public sealed class SpecKitVersionTests
{
    [Theory]
    [InlineData("1.0.5.dev0", "1.0.5")]
    [InlineData("1.1.0rc1", "1.1.0")]
    [InlineData("1.0.5+local", "1.0.5")]
    [InlineData("1.0.5", null)]
    [InlineData("main", null)]
    public void APreReleaseIsComparedWithItsRelease(string installed, string? release) => Assert.Equal(release, SpecKitUpgrade.ReleaseOf(installed));

    [Theory]
    [InlineData("1.2.3", true)]
    [InlineData("1.1.4", true)]
    [InlineData("1.2.0.dev0", true)]
    [InlineData("2.0.0", true)]
    [InlineData("1.1.3", false)]
    [InlineData("1.1.3.dev0", false)]
    [InlineData("1.1.0", false)]
    [InlineData("1.0.13", false)]
    [InlineData("unknown", false)]
    public void OnlyALaterReleaseIsNewerThanThePinnedVersion(string installed, bool newer) => Assert.Equal(newer, SpecKitUpgrade.IsNewerThanPinned(installed));
}
