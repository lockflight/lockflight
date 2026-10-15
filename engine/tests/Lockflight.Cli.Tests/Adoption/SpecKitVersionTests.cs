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
}
