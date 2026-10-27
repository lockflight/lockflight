using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Testing;

public sealed class InProcessRunnerTests
{
    [Theory]
    [InlineData("1.1.0.0", "-xml")]
    [InlineData("3.1.0.0", "-xml")]
    [InlineData("3.2.2.0", "-xml")]
    [InlineData("4.0.0.0", "-result-xml")]
    [InlineData("4.0.2.0", "-result-xml")]
    [InlineData("5.0.0.0", "-result-xml")]
    public void TheXmlReportSwitchFollowsTheRunnerVersion(string version, string expected) =>
        Assert.Equal(expected, InProcessRunner.ResultXmlSwitch(Version.Parse(version)));

    [Fact]
    public void AnUnknownRunnerVersionGetsTheCurrentSwitch() =>
        Assert.Equal("-result-xml", InProcessRunner.ResultXmlSwitch(null));

    [Fact]
    public void TheRunnerVersionIsReadFromTheTestAssemblysReferences() =>
        Assert.Equal(typeof(FactAttribute).Assembly.GetName().Version, InProcessRunner.VersionOf(typeof(InProcessRunnerTests).Assembly.Location));

    [Fact]
    public void AnAssemblyWithoutTheRunnerHasNoRunnerVersion() =>
        Assert.Null(InProcessRunner.VersionOf(typeof(InProcessRunner).Assembly.Location));

    [Fact]
    public void AnUnreadableAssemblyHasNoRunnerVersion()
    {
        string file = Path.GetTempFileName();
        try
        {
            Assert.Null(InProcessRunner.VersionOf(file));
        }
        finally
        {
            File.Delete(file);
        }
    }
}
