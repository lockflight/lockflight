using Lockflight.Cli.Gates;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Mascot;

namespace Lockflight.Cli.Tests.Mascot;

public sealed class MascotDisplayTests
{
    [Theory]
    [InlineData(true, null, null, null, true)]
    [InlineData(false, null, null, null, false)]
    [InlineData(true, "1", null, null, false)]
    [InlineData(true, null, "1", null, false)]
    [InlineData(true, null, null, "0", false)]
    public void LeviIsDrawnOnlyForAPersonAtATerminal(bool terminal, string? claudeCode, string? noColor, string? mascot, bool shown)
    {
        Dictionary<string, string> variables = [];
        Add(variables, EnvironmentVariables.ClaudeCode, claudeCode);
        Add(variables, EnvironmentVariables.NoColor, noColor);
        Add(variables, EnvironmentVariables.Mascot, mascot);
        CliContext context = new(TextReader.Null, TextWriter.Null, TextWriter.Null, "/", TimeProvider.System)
        {
            Environment = EnvironmentReader.Of(variables),
            OutputIsTerminal = terminal,
        };

        Assert.Equal(shown, MascotDisplay.ShouldShow(context));
    }

    [Theory]
    [InlineData((int)Verdict.Pass, (int)MascotPose.Pass)]
    [InlineData((int)Verdict.NotEarned, (int)MascotPose.NotEarned)]
    [InlineData((int)Verdict.Fail, (int)MascotPose.Fail)]
    [InlineData((int)Verdict.Error, (int)MascotPose.Fail)]
    public void EachVerdictHasItsPose(int verdict, int pose) => Assert.Equal((MascotPose)pose, MascotDisplay.For((Verdict)verdict));

    [Fact]
    public async Task CommandOutputReadByAProgramHasNoArt()
    {
        DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-mascot-");
        try
        {
            (_, string output, _) = await TestConsole.RunAsync(["doctor"], "", root.FullName);

            Assert.DoesNotContain("▀", output, StringComparison.Ordinal);
        }
        finally
        {
            TempDirectories.DeleteRecursively(root);
        }
    }

    private static void Add(Dictionary<string, string> variables, string name, string? value)
    {
        if (value is not null)
        {
            variables[name] = value;
        }
    }
}
