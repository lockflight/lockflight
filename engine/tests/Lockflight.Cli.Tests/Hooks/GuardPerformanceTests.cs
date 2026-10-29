using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hooks;
using Lockflight.Cli.Hooks.Adapters;
using Lockflight.Cli.Hooks.Guard;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Tests.Hooks;

public sealed class GuardPerformanceTests(GuardPerformanceTree tree) : IClassFixture<GuardPerformanceTree>
{
    // Counted, not timed: a decision's directory reads are the same on every machine, so the bound can be tight without
    // flaking on a loaded runner. "rm -rf node_modules" walks the whole tree once; a wildcard below it is judged by one walk of the
    // directory before the wildcard, never by expanding it.
    [Theory]
    [InlineData("rm -rf node_modules/*")]
    [InlineData("rm -rf node_modules/p1*")]
    [InlineData("rm -rf node_modules/*/lib")]
    [InlineData("rm node_modules/*/lib/*.js")]
    public async Task AWildcardOverThousandsOfDirectoriesReadsTheTreeOnceAtMost(string command)
    {
        DirectoryReads oneWalk = await DecideAsync("rm -rf node_modules");
        DirectoryReads wildcard = await DecideAsync(command);

        Assert.InRange(wildcard.Entries, 0, oneWalk.Entries);
        Assert.InRange(wildcard.Listings, 0, oneWalk.Listings);
    }

    [Theory]
    [InlineData("rm -f *.log")]
    [InlineData("rm -rf build/*")]
    public async Task AWildcardBesideALargeTreeDoesNotEnterIt(string command)
    {
        DirectoryReads reads = await DecideAsync(command);

        Assert.InRange(reads.Entries, 0, GuardPerformanceTree.PackageCount / 10);
    }

    private async Task<DirectoryReads> DecideAsync(string command)
    {
        string payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["cwd"] = tree.Root,
            ["tool_name"] = "Bash",
            ["tool_input"] = new Dictionary<string, string> { ["command"] = command },
        });
        DirectoryReads reads = new();
        CliContext context = new(new StringReader(string.Empty), TextWriter.Null, TextWriter.Null, tree.Root, TimeProvider.System)
        {
            Environment = EnvironmentReader.Of(new Dictionary<string, string>()),
        };

        HookDecision decision = await GuardRules.DecideAsync(
            ClaudeCodeAdapter.Parse(payload, projectDirectory: null)!, ManifestLocator.Discover(tree.Root).Manifest!, context, reads, TestContext.Current.CancellationToken);

        Assert.Equal(HookOutcome.Allow, decision.Outcome);
        return reads;
    }
}
