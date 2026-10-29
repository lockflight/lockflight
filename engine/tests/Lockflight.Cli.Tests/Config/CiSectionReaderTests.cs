using Lockflight.Cli.Config;

namespace Lockflight.Cli.Tests.Config;

public sealed class CiSectionReaderTests
{
    [Fact]
    public void WithoutACiSectionTheDefaultsApply()
    {
        CiSettings ci = Parse("version: 1\n").Manifest!.Ci;

        Assert.Equal(CiSettings.Default, ci);
        Assert.Equal(10, ci.MaxRedCommits);
        Assert.Equal(45, ci.TimeoutMinutes);
        Assert.Equal(2, ci.FlakyRetries);
        Assert.Equal(["docs/**", "*.md", ".github/ISSUE_TEMPLATE/**"], ci.LowRiskPaths);
        Assert.True(ci.RedCommits);
    }

    [Fact]
    public void EveryKeyIsRead()
    {
        CiSettings ci = Parse("""
            version: 1
            ci:
              maxRedCommits: 4
              timeoutMinutes: 90
              flakyRetries: 0
              lowRiskPaths: [docs/**, site/**]
              testInputs: [testdata/**]
              redCommits: false
            """).Manifest!.Ci;

        Assert.Equal(4, ci.MaxRedCommits);
        Assert.Equal(90, ci.TimeoutMinutes);
        Assert.Equal(0, ci.FlakyRetries);
        Assert.Equal(["docs/**", "site/**"], ci.LowRiskPaths);
        Assert.Equal(["testdata/**"], ci.TestInputs);
        Assert.False(ci.RedCommits);
        Assert.True(ci.FromManifest);
    }

    [Theory]
    [InlineData("  maxRedCommits: 0\n", "'ci.maxRedCommits' must be a whole number from 1 to 50")]
    [InlineData("  timeoutMinutes: 9999\n", "'ci.timeoutMinutes' must be a whole number of minutes from 1 to 360")]
    [InlineData("  flakyRetries: -1\n", "'ci.flakyRetries' must be a whole number from 0 to 5")]
    [InlineData("  lowRiskPaths: [docs/**, docs/**]\n", "'ci.lowRiskPaths' must be a list of non-empty path globs without duplicates")]
    [InlineData("  testInputs: testdata\n", "'ci.testInputs' must be a list")]
    [InlineData("  redCommits: yes\n", "'ci.redCommits' must be true or false")]
    [InlineData("  maxRedCommit: 3\n", "unknown key 'ci.maxRedCommit'")]
    public void AnInvalidValueIsAnErrorWithItsPlace(string line, string message)
    {
        ManifestLoadResult result = Parse("version: 1\nci:\n" + line);

        Assert.NotNull(result.Error);
        Assert.Contains(message, result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(3, result.Error.Line);
    }

    private static ManifestLoadResult Parse(string yaml) => new ManifestParser("/repo", "/repo/lockflight.yaml").Parse(yaml);
}
