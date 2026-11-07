using Lockflight.Cli.Ci;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Ci;

public sealed class JudgeInCiTests
{
    [Theory]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "--platform", "local" }, false)]
    [InlineData(new[] { "--platform", "github" }, true)]
    [InlineData(new[] { "--platform", "bitbucket" }, true)]
    [InlineData(new[] { "--platform", "nowhere" }, false)]
    public void OnlyACiPlatformMakesTheJudgeACiRun(string[] arguments, bool inCi)
    {
        CommandOptions options = CommandOptions.Parse(arguments, [JudgeInputsReader.PlatformOption], []).Options!;

        Assert.Equal(inCi, JudgeInputsReader.InCi(options));
    }
}
