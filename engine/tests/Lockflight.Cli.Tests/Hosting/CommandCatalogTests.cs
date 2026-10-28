using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Hosting;

public sealed class CommandCatalogTests : IDisposable
{
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("lockflight-commands-");

    public static TheoryData<string> ParsedCommands() =>
        [.. CommandRegistry.All.Where(command => command is { Listing: CommandListing.Listed, Parsing: OptionParsing.Shared }).Select(command => command.Name)];

    [Theory]
    [MemberData(nameof(ParsedCommands))]
    public async Task EveryCatalogedCommandAcceptsExactlyTheCatalogedOptions(string name)
    {
        CatalogedCommand command = CommandCatalog.All.Single(entry => entry.Name == name);
        string[] arguments = command.TakesArguments ? ["placeholder"] : [];

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync([.. command.Words, .. arguments, "--no-such-option"], string.Empty, directory.FullName);

        Assert.Equal(ExitCode.Config, exitCode);
        string accepted = error.Contains("; use ", StringComparison.Ordinal) ? error[(error.IndexOf("; use ", StringComparison.Ordinal) + "; use ".Length)..] : string.Empty;
        string[] parsed = [.. accepted.Split('.')[0].Split(", ", StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal)];
        Assert.Equal([.. command.Options.Order(StringComparer.Ordinal)], parsed);
    }

    [Fact]
    public async Task EveryCatalogedCommandAndOptionIsInTheHelpText()
    {
        (_, string help, _) = await TestConsole.RunAsync(["--help"], string.Empty, directory.FullName);

        foreach (CommandDescriptor command in CommandRegistry.All.Where(entry => entry.Listing == CommandListing.Listed && entry.Name is not ("update" or "version")))
        {
            string[] lines = [.. help.Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith(command.Words[0] + " ", StringComparison.Ordinal) || line == command.Words[0])];
            Assert.True(
                lines.Any(line => command.Words.All(word => line.Split(' ', '|').Contains(word))),
                $"no help line names '{command.Name}'");
            foreach (string option in command.ValueOptions.Concat(command.FlagOptions))
            {
                Assert.Contains(option.Split('=')[0], help, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void TheLongestMatchingCommandIsFound()
    {
        Assert.Equal("review status", CommandCatalog.Match(["review", "status", "--feature", "x"])?.Name);
        Assert.Equal("review", CommandCatalog.Match(["review", "--base", "main"])?.Name);
        Assert.Null(CommandCatalog.Match(["gate", "check"]));
    }

    public void Dispose() => TempDirectories.DeleteRecursively(directory);
}
