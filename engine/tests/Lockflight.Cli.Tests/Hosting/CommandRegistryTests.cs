using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Hosting;

public sealed class CommandRegistryTests
{
    [Fact]
    public void NoTwoCommandsShareTheirWords() =>
        Assert.Equal(CommandRegistry.All.Count, CommandRegistry.All.Select(command => command.Name).Distinct(StringComparer.Ordinal).Count());

    [Fact]
    public void EveryHumanOnlyCommandIsRefusedAndKnownToTheGuard()
    {
        foreach (CommandDescriptor command in CommandRegistry.All.Where(entry => entry.Audience == CommandAudience.Human))
        {
            string[] line = command.Listing == CommandListing.Listed ? [.. command.Words] : [.. command.Words, "xyz"];
            Assert.True(HumanOnlyCommands.IsReserved(line), $"'{command.Name}' is not refused");
            Assert.NotNull(HumanOnlyCommands.FindInWords(["lockflight", .. line], honorHelp: true));
        }
    }

    [Fact]
    public void EveryReservedGroupIsTheFirstWordOfACommand()
    {
        foreach (ReservedGroup group in CommandRegistry.ReservedGroups)
        {
            Assert.Contains(CommandRegistry.All, command => command.Words[0] == group.Word);
            Assert.All(group.AgentVerbs, verb => Assert.Equal(CommandAudience.Anyone, CommandRegistry.Match([group.Word, verb])?.Audience));
        }
    }

    [Fact]
    public void AGroupsCatchAllNeedsAVerbAndIsNotListed()
    {
        foreach (CommandDescriptor catchAll in CommandRegistry.All.Where(entry => entry.Listing == CommandListing.Unlisted))
        {
            Assert.Equal(CommandOperands.AtLeastOne, catchAll.Operands);
            Assert.Contains(CommandRegistry.All, command => command.Listing == CommandListing.Listed && command.Words.Count > 1 && command.Words[0] == catchAll.Words[0]);
        }
    }

    [Fact]
    public void TheLongestMatchingCommandIsFound()
    {
        Assert.Equal("review status", CommandRegistry.Match(["review", "status", "--feature", "x"])?.Name);
        Assert.Equal("review", CommandRegistry.Match(["review", "--base", "main"])?.Name);
        Assert.Equal("redgreen", CommandRegistry.Match(["redgreen", "xyz"])?.Name);
        Assert.Null(CommandRegistry.Match(["gate", "check"]));
    }
}
