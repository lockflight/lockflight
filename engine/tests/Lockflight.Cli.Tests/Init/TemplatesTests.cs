using Lockflight.Cli.Config;
using Lockflight.Cli.Init;

namespace Lockflight.Cli.Tests.Init;

public sealed class TemplatesTests
{
    [Fact]
    public void TheManifestCommentNamesTheProfileThatWroteIt()
    {
        Assert.Contains("lite profile", Templates.Manifest(InitProfile.Lite), StringComparison.Ordinal);
        Assert.Contains("standard profile", Templates.Manifest(InitProfile.Standard), StringComparison.Ordinal);
        Assert.DoesNotContain("lite profile", Templates.Manifest(InitProfile.Standard), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("lite")]
    [InlineData("standard")]
    public void EachProfilesManifestLoads(string profileName)
    {
        Assert.True(InitProfileNames.TryParse(profileName, out InitProfile profile));

        ManifestLoadResult loaded = new ManifestParser("/project", "/project/lockflight.yaml").Parse(Templates.Manifest(profile));

        Assert.NotNull(loaded.Manifest);
        Assert.False(loaded.Manifest.StopGate);
    }

    [Theory]
    [InlineData(".editorconfig")]
    [InlineData("BannedSymbols")]
    [InlineData("ruleset")]
    public void TheWorkflowTakesCodeStyleConfigFromTheBase(string name)
    {
        string workflow = Templates.Workflow(["10.0.x"]);

        Assert.Contains("git ls-tree -r --name-only \"$BASE\"", workflow, StringComparison.Ordinal);
        Assert.Contains(name, workflow, StringComparison.Ordinal);
    }
}
