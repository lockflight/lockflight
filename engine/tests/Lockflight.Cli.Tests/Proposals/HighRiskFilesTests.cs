using Lockflight.Cli.Proposals;

namespace Lockflight.Cli.Tests.Proposals;

public sealed class HighRiskFilesTests
{
    [Theory]
    [InlineData("Directory.Build.props")]
    [InlineData("src/Directory.Packages.props")]
    [InlineData("build/Common.targets")]
    [InlineData("src/App/App.csproj")]
    [InlineData(".editorconfig")]
    [InlineData("src/.globalconfig")]
    [InlineData("rules/Team.ruleset")]
    [InlineData("BannedSymbols.txt")]
    [InlineData(".claude/hooks/guard.sh")]
    [InlineData(".claude/settings.json")]
    [InlineData(".claude/settings.local.json")]
    [InlineData(".github/workflows/ci.yml")]
    [InlineData("NuGet.config")]
    [InlineData("src/nuget.config")]
    [InlineData("global.json")]
    [InlineData("lockflight.yaml")]
    public void ControlFilesAreHighRisk(string relativePath) => Assert.NotNull(HighRiskFiles.Reason(relativePath));

    [Theory]
    [InlineData("src/App/Program.cs")]
    [InlineData("README.md")]
    [InlineData(".claude/skills/review/SKILL.md")]
    [InlineData("docs/global.json.md")]
    public void OrdinaryFilesAreNotHighRisk(string relativePath) => Assert.Null(HighRiskFiles.Reason(relativePath));
}
