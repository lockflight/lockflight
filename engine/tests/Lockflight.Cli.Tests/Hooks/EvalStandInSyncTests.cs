using Lockflight.Cli.Hooks;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Hooks;

public sealed class EvalStandInSyncTests
{
    private const string NotWithBangVariable = "$notbang";

    [Fact]
    public void EveryEvalStandInEngineGivesTheRealBannersNotesOnHumanDecisionsAndProposals()
    {
        string plugins = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ShimLocation.Path)!, "..", ".."));
        string[] standIns = [.. Directory.EnumerateFiles(plugins, "scaffold.sh", SearchOption.AllDirectories)
            .Where(scaffold => File.ReadAllText(scaffold).Contains("\"hook session-start\")", StringComparison.Ordinal))];
        string note = SessionBanner.HumanDecisionsNote.Replace(HumanOnlyCommands.NotWithBang, NotWithBangVariable, StringComparison.Ordinal);
        string notWithBang = $"notbang=\"{HumanOnlyCommands.NotWithBang.Replace("`", "\\`", StringComparison.Ordinal)}\"";

        Assert.NotEmpty(standIns);
        Assert.All(standIns, scaffold =>
        {
            string text = File.ReadAllText(scaffold);
            Assert.Contains(note, text, StringComparison.Ordinal);
            Assert.Contains(notWithBang, text, StringComparison.Ordinal);
            Assert.Contains(SessionBanner.ProposeNote, text, StringComparison.Ordinal);
        });
    }
}
