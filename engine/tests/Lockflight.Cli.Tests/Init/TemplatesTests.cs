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

    [Fact]
    public void TheWorkflowInstallsThePinnedEngineOutsideTheRepositorysGlobalJson()
    {
        string workflow = Templates.Workflow(["8.0.x", "10.0.x"]);

        Assert.Contains("(cd \"$RUNNER_TEMP\" && dotnet tool install Lockflight.Cli --tool-path", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet tool restore", workflow, StringComparison.Ordinal);
        Assert.Contains("  lockflight gate run --profile pr", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet lockflight", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void AManualWorkflowForADevelopmentPinSaysWhyItRunsOnlyByHand()
    {
        string workflow = Templates.Workflow(["10.0.x"], CiTrigger.Manual, developmentPin: true);

        Assert.Contains("on:\n  workflow_dispatch:\n", workflow, StringComparison.Ordinal);
        Assert.Contains("CI during the preview", workflow, StringComparison.Ordinal);
        Assert.Contains("development build", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void AManualWorkflowForAPublishedPinNamesTheOption()
    {
        string workflow = Templates.Workflow(["10.0.x"], CiTrigger.Manual, developmentPin: false);

        Assert.Contains("lockflight init --ci manual", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("CI during the preview", workflow, StringComparison.Ordinal);
    }
}
