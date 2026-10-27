using System.Text.Json.Nodes;
using Lockflight.Cli.Config;
using Lockflight.Cli.Init;

namespace Lockflight.Cli.Tests.Init;

public sealed class ToolManifestWriterTests
{
    [Fact]
    public void ANewManifestPinsTheRunningEngine()
    {
        (string json, bool changed) = ToolManifestWriter.Pin(null);

        Assert.True(changed);
        Assert.Equal(ToolPin.RunningVersion, JsonNode.Parse(json)!["tools"]!["lockflight.cli"]!["version"]!.GetValue<string>());
    }

    [Fact]
    public void ANewerPinIsKept()
    {
        const string existing = """{"tools":{"lockflight.cli":{"version":"99.0.0"}}}""";

        Assert.Equal((existing, false), ToolManifestWriter.Pin(existing));
        Assert.Contains("99.0.0", ToolManifestWriter.Problem(existing), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"tools":{"lockflight.cli":{"version":"0.0.0-dev.1"}}}""")]
    [InlineData("""{"tools":{"LOCKFLIGHT.CLI":{"version":"0.0.0-alpha"}}}""")]
    [InlineData("""{"tools":{}}""")]
    [InlineData("""{}""")]
    public void AnOlderOrMissingPinIsNotKept(string existing)
    {
        Assert.Null(ToolManifestWriter.Problem(existing));
        Assert.True(ToolManifestWriter.Pin(existing).Changed);
    }

    [Theory]
    [InlineData("""{"tools":{"lockflight.cli":{"version":1}}}""")]
    [InlineData("""{"tools":{"lockflight.cli":{"version":"latest"}}}""")]
    public void APinThatIsNotAVersionIsKeptAsIs(string existing)
    {
        Assert.NotNull(ToolManifestWriter.Problem(existing));
        Assert.False(ToolManifestWriter.Pin(existing).Changed);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"tools":[]}""")]
    [InlineData("""{"tools":{"lockflight.cli":"1.0.0"}}""")]
    public void AManifestOfTheWrongShapeIsAProblem(string existing) => Assert.NotNull(ToolManifestWriter.Problem(existing));

    [Fact]
    public void AKeyInAnotherCaseIsUpdatedWithoutASecondEntry()
    {
        (string json, _) = ToolManifestWriter.Pin("""{"tools":{"Lockflight.Cli":{"version":"0.0.0-alpha"},"other":{"version":"1.0.0"}}}""");

        JsonObject tools = JsonNode.Parse(json)!["tools"]!.AsObject();
        Assert.Equal(["Lockflight.Cli", "other"], tools.Select(tool => tool.Key));
        Assert.Equal(ToolPin.RunningVersion, tools["Lockflight.Cli"]!["version"]!.GetValue<string>());
    }
}
