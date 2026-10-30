using Lockflight.Cli.Ci;
using Lockflight.Cli.Mascot;
using Lockflight.Cli.Tests.Schemas;

namespace Lockflight.Cli.Tests.Mascot;

/// <summary>
/// The PNG badges the CI comment and job summary show are Levi's pixels, scaled: each committed file must decode to what the art
/// draws now. <c>LOCKFLIGHT_UPDATE_GOLDENS=1</c> writes them again from the art.
/// </summary>
public sealed class VerdictBadgeTests
{
    private const string UpdateVariable = "LOCKFLIGHT_UPDATE_GOLDENS";

    private static readonly MascotPose[] Verdicts = [MascotPose.Pass, MascotPose.NotEarned, MascotPose.Fail, MascotPose.Blocked];

    public static TheoryData<string, string> Badges() =>
        [.. Verdicts.SelectMany(pose => Enum.GetValues<BadgeSize>().Select(size => (pose.ToString(), size.ToString())))];

    [Theory]
    [MemberData(nameof(Badges))]
    public void EachCommittedBadgeIsLevisArtForItsVerdict(string poseName, string sizeName)
    {
        MascotPose pose = Enum.Parse<MascotPose>(poseName);
        BadgeSize size = Enum.Parse<BadgeSize>(sizeName);
        string path = Path.Combine(SchemaFiles.RepositoryRoot, VerdictBadge.Directory, VerdictBadge.FileName(pose, size));
        (int width, int height, byte[] rgba) = Draw(pose, VerdictBadge.Scale(size));
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, PixelPng.Encode(width, height, rgba));
            return;
        }

        Assert.True(File.Exists(path), $"{path} does not exist; run these tests with {UpdateVariable}=1 to write it");
        (int Width, int Height, byte[] Rgba)? committed = PixelPng.Decode(File.ReadAllBytes(path));
        Assert.True(committed is not null, $"{path} is not a badge this test wrote; run these tests with {UpdateVariable}=1");
        Assert.Equal((width, height), (committed.Value.Width, committed.Value.Height));
        Assert.True(rgba.AsSpan().SequenceEqual(committed.Value.Rgba), $"{path} is not the art of {pose}; run these tests with {UpdateVariable}=1");
    }

    [Theory]
    [InlineData("PASS", false, "pass")]
    [InlineData("NOT-EARNED", false, "not-earned")]
    [InlineData("NOT-EARNED", true, "blocked")]
    [InlineData("FAIL", false, "fail")]
    [InlineData("ERROR", false, "fail")]
    public void EachVerdictHasItsPose(string verdict, bool waitsForAPerson, string pose) =>
        Assert.Equal(pose, VerdictBadge.Name(VerdictBadge.PoseOf(verdict, waitsForAPerson)));

    [Theory]
    [InlineData("0.3.0", "v0.3.0")]
    [InlineData("0.3.1-rc.2", "v0.3.1-rc.2")]
    [InlineData("0.0.0-local", "main")]
    [InlineData("0.3.0-dev.12", "main")]
    [InlineData("0.0.0-ci.10", "main")]
    [InlineData("0.3.0/../../evil", "main")]
    [InlineData("0.3.0\n", "main")]
    [InlineData(null, "main")]
    public void ABadgeIsServedFromTheEnginesReleaseTagElseMain(string? version, string reference) =>
        Assert.Equal($"https://raw.githubusercontent.com/lockflight/lockflight/{reference}/docs/assets/badges/verdict-pass.png", VerdictBadge.Url(version, MascotPose.Pass, BadgeSize.Small));

    /// <summary>The pose's pixels scaled by <paramref name="scale"/>, transparent where the art has none.</summary>
    private static (int Width, int Height, byte[] Rgba) Draw(MascotPose pose, int scale)
    {
        string[] pixels = MascotArt.Pixels(pose);
        int width = MascotArt.Width * scale;
        int height = MascotArt.Height * scale;
        byte[] rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (MascotArt.ColorOf(pixels[y / scale][x / scale]) is (byte red, byte green, byte blue))
                {
                    int index = ((y * width) + x) * 4;
                    (rgba[index], rgba[index + 1], rgba[index + 2], rgba[index + 3]) = (red, green, blue, 0xFF);
                }
            }
        }

        return (width, height, rgba);
    }
}
