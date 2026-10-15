using Lockflight.Cli.Config;

namespace Lockflight.Cli.Tests.Config;

public sealed class ToolPinTests
{
    [Theory]
    [InlineData("0.1.0-alpha.1", "0.1.0-alpha.1+3f2c1a9", true)]
    [InlineData("0.1.0-ALPHA.1", "0.1.0-alpha.1", true)]
    [InlineData("0.1.0-alpha.1", "0.1.0", false)]
    [InlineData("0.1.0", "0.1.1", false)]
    public void PrereleaseVersionsMatchTheirPinIgnoringBuildMetadata(string pinned, string running, bool expected) =>
        Assert.Equal(expected, ToolPin.Matches(pinned, running));

    [Fact]
    public void TheRunningVersionKeepsItsPrereleaseSuffix() =>
        Assert.DoesNotContain("+", ToolPin.RunningVersion, StringComparison.Ordinal);

    [Theory]
    [InlineData("{ not json", 2)]
    [InlineData("[]", 2)]
    [InlineData("""{ "tools": 1 }""", 2)]
    [InlineData("""{ "tools": { "lockflight.cli": { "version": 7 } } }""", 2)]
    [InlineData("""{ "tools": { "lockflight.cli": { "version": null } } }""", 2)]
    [InlineData("""{ "tools": { "other": { "version": "1.0.0" } } }""", 0)]
    [InlineData("""{ "tools": { "Lockflight.Cli": { "version": "1.2.3" } } }""", 1)]
    public void ReadingThePinNeverThrows(string toolManifest, int expectedStatus)
    {
        DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-pin-");
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "dotnet-tools.json"), toolManifest);

            ToolPinStatus expected = (ToolPinStatus)expectedStatus;
            ToolPinReading reading = ToolPin.ReadPin(root.FullName);

            Assert.Equal(expected, reading.Status);
            Assert.Equal(expected == ToolPinStatus.Unreadable, reading.Problem is not null);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
