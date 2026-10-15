using Lockflight.Cli.Mascot;

namespace Lockflight.Cli.Tests.Mascot;

public sealed class MascotArtTests
{
    [Fact]
    public void EveryPoseIsTheSameSizeAndUsesOnlyPaletteKeys()
    {
        foreach (MascotPose pose in Enum.GetValues<MascotPose>())
        {
            string[] pixels = MascotArt.Pixels(pose);

            Assert.Equal(MascotArt.Height, pixels.Length);
            Assert.All(pixels, row => Assert.Equal(MascotArt.Width, row.Length));
            Assert.All(pixels, row => Assert.Matches("^[.CADBKWLPGRQS]+$", row));
        }
    }

    [Fact]
    public void EachVerdictLooksDifferent()
    {
        string[] poses = [.. Enum.GetValues<MascotPose>().Select(pose => string.Join('\n', MascotArt.Pixels(pose)))];

        Assert.Equal(poses.Length, poses.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void TheVerdictPropsCarryTheirColors()
    {
        Assert.Contains(MascotArt.Pixels(MascotPose.Pass), row => row.Contains('G', StringComparison.Ordinal));
        Assert.Contains(MascotArt.Pixels(MascotPose.Fail), row => row.Contains('R', StringComparison.Ordinal));
        Assert.Contains(MascotArt.Pixels(MascotPose.NotEarned), row => row.Contains('Q', StringComparison.Ordinal));
    }

    [Fact]
    public void ATerminalDrawingUsesTwoPixelRowsPerLine()
    {
        IReadOnlyList<string> lines = MascotArt.Render(MascotPose.Pass);

        Assert.Equal(MascotArt.Height / 2, lines.Count);
        Assert.All(lines, line => Assert.EndsWith(MascotArt.Reset, line, StringComparison.Ordinal));
    }
}
