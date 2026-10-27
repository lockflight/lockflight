using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hooks;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Tests.Hooks;

public sealed class GuardPerformanceTests(GuardPerformanceTree tree) : IClassFixture<GuardPerformanceTree>
{
    // Counted, not timed: a decision's directory reads are the same on every machine, so the bound can be tight without
    // flaking on a loaded runner. "rm -rf node_modules" walks the whole tree once; a wildcard over it may read each entry a few
    // times (its expansion lists the siblings again) but never once per sibling.
    private const int MaxTimesOneWalk = 3;
    private const int MaxSharedListings = 4;

    /// <param name="separateWalks">Trees walked on their own: none when the siblings are most of the directory and one walk of it
    /// serves them all; one per match when they are a small part of it (node_modules/p1* matches 611 of 1500).</param>
    [Theory]
    [InlineData("rm -rf node_modules/*", 0)]
    [InlineData("rm -rf node_modules/p1*", 611)]
    [InlineData("rm -rf node_modules/*/lib", 0)]
    public async Task AWildcardOverThousandsOfDirectoriesReadsTheTreeAFewTimesAtMost(string command, int separateWalks)
    {
        DirectoryReads oneWalk = await DecideAsync("rm -rf node_modules");
        DirectoryReads wildcard = await DecideAsync(command);

        Assert.InRange(wildcard.Entries, GuardPerformanceTree.PackageCount, MaxTimesOneWalk * oneWalk.Entries);
        Assert.InRange(wildcard.Listings, 1, separateWalks + MaxSharedListings);
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
        ProtectedPathGuard guard = new(TextWriter.Null, EnvironmentReader.Of(new Dictionary<string, string>()), reads);

        HookDecision decision = await guard.DecideAsync(HookInput.Parse(payload)!, ManifestLocator.Discover(tree.Root).Manifest!);

        Assert.Equal(HookOutcome.Allow, decision.Outcome);
        return reads;
    }
}
