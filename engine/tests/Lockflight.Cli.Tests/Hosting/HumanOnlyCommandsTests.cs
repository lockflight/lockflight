using Lockflight.Cli.Hooks;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Tests.Init;

namespace Lockflight.Cli.Tests.Hosting;

public sealed class HumanOnlyCommandsTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-human-only-");

    [Theory]
    [InlineData("dotnet lockflight baseline build", "baseline build")]
    [InlineData("lockflight  baseline tests", "baseline tests")]
    [InlineData("lockflight --verbose approve spec", "approve spec")]
    [InlineData(".lockflight/bin/lockflight.exe approve tasks --feature f1", "approve tasks")]
    [InlineData("cd src && \"/opt/tools/lockflight\" redgreen reset", "redgreen reset")]
    [InlineData(".lockflight/bin/lockflight redgreen amend ShippingTests.FirstKilogramCostsFive --reason \"wrong rate\"", "redgreen amend")]
    [InlineData("echo ok\ndotnet lockflight baseline deps", "baseline deps")]
    [InlineData("(lockflight approve spec)", "approve spec")]
    [InlineData("echo $(lockflight approve tasks)", "approve tasks")]
    [InlineData(".lockflight/bin/lockflight review dismiss 0123456789ab --reason \"intended\"", "review dismiss")]
    [InlineData("bash -c \"lockflight approve spec\"", "approve spec")]
    [InlineData("eval 'dotnet lockflight approve tasks'", "approve tasks")]
    [InlineData("sh -ec 'cd src && lockflight baseline build'", "baseline build")]
    public void ReservedCommandsAreFoundWhateverTheSpelling(string command, string expected) =>
        Assert.Equal(expected, HumanOnlyCommands.FindIn(command, ShellDialect.Posix));

    [Theory]
    [InlineData("dotnet lockflight approve verify --base main")]
    [InlineData("lockflight redgreen red")]
    [InlineData("lockflight review --base main")]
    [InlineData("lockflight review status --feature 001-orders")]
    [InlineData("lockflight gate run --profile pr")]
    [InlineData("echo approve && lockflight phase check tasks")]
    public void OtherCommandsPass(string command) => Assert.Null(HumanOnlyCommands.FindIn(command, ShellDialect.Posix));

    [Theory]
    [InlineData("lockflight approve --help")]
    [InlineData("lockflight approve spec -h")]
    [InlineData("lockflight approve spec --help --feature 001-orders")]
    [InlineData(".lockflight/bin/lockflight redgreen amend --help")]
    [InlineData("dotnet lockflight baseline tests --help")]
    [InlineData("cd src && \"/opt/tools/lockflight\" review dismiss -h")]
    [InlineData("LOCKFLIGHT_DEBUG=1 lockflight update --help")]
    [InlineData("bash -c 'lockflight baseline deps --help'")]
    public void AskingAReservedCommandForHelpPasses(string command) => Assert.Null(HumanOnlyCommands.FindIn(command, ShellDialect.Posix));

    [Theory]
    [InlineData("lockflight approve spec --help=no", "approve spec")]
    [InlineData("lockflight approve spec # --help", "approve spec")]
    [InlineData("lockflight approve spec > --help", "approve spec")]
    [InlineData("lockflight approve spec; lockflight --help", "approve spec")]
    [InlineData("echo --help | lockflight approve spec", "approve spec")]
    [InlineData("H=--help; lockflight approve spec $H", "approve spec")]
    [InlineData("find . -maxdepth 0 -exec lockflight approve spec \\; -name --help", "approve spec")]
    [InlineData("xargs lockflight approve spec --help", "approve spec")]
    [InlineData("env lockflight approve spec --help", "approve spec")]
    [InlineData("lockflight approve --help lockflight approve spec", "approve spec")]
    [InlineData("lockflight() { command lockflight \"${@/--help/}\"; }; lockflight approve spec --help", "approve spec")]
    [InlineData("function lockflight { command lockflight \"${@/--help/}\"; }; lockflight approve spec -h", "approve spec")]
    [InlineData("bash -c 'lockflight() { command lockflight \"${@/--help/}\"; }; lockflight approve spec --help'", "approve spec")]
    public void HelpThatMayNotReachTheEngineIsNoExemption(string command, string expected) =>
        Assert.Equal(expected, HumanOnlyCommands.FindIn(command, ShellDialect.Posix));

    [Fact]
    public void TheRefusalNamesTheSeparateTerminalBeforeTheCommandAndPointsAtTheSessionCommand()
    {
        string refusal = HumanOnlyCommands.Refusal(["approve", "spec", "--feature", "001-orders"], "/work/shop", EngineCommand.Linked);

        Assert.Contains("'approve spec' records a human decision", refusal, StringComparison.Ordinal);
        Assert.Contains(
            "Open a separate terminal window (not with `!` in Claude Code: that runs inside the session and is refused) and run `cd '/work/shop' && .lockflight/bin/lockflight approve spec --feature 001-orders`.",
            refusal,
            StringComparison.Ordinal);
        Assert.EndsWith("Or type /lockflight:approve spec in Claude Code.", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("\n  cd", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void ATerminalOnlyCommandHasNoSessionAlternative()
    {
        string refusal = HumanOnlyCommands.Refusal(["update"], "/work/shop", EngineCommand.UserInstall);

        Assert.EndsWith("and run `cd '/work/shop' && ~/.lockflight/bin/lockflight update`.", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("/lockflight:", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCommandToCopyQuotesArgumentsTheShellWouldSplit() =>
        Assert.Contains(
            "`cd '/work/my shop' && dotnet lockflight approve tasks --feature 'it'\\''s feature'`",
            HumanOnlyCommands.Refusal(["approve", "tasks", "--feature", "it's feature"], "/work/my shop", EngineCommand.Tool),
            StringComparison.Ordinal);

    [Theory]
    [InlineData("approve tasks", "ask the user to type /lockflight:approve tasks in Claude Code, or open a separate terminal window")]
    [InlineData("baseline tests", "ask the user to type /lockflight:baseline tests in Claude Code, or open a separate terminal window")]
    [InlineData("redgreen reset", "ask the user to type /lockflight:reset in Claude Code, or open a separate terminal window")]
    [InlineData("redgreen amend <test> --reason \"<why>\"", "ask the user to type /lockflight:amend <test> --reason <why> in Claude Code, or open a separate terminal window")]
    [InlineData("update", "ask the user to open a separate terminal window")]
    public void AHandOffPutsTheSessionCommandOrTheSeparateTerminalFirst(string command, string start)
    {
        string handOff = HumanOnlyCommands.AskTheUser(command, root.FullName);

        Assert.StartsWith(start, handOff, StringComparison.Ordinal);
        Assert.Contains($"(not with `!` in Claude Code: that runs inside the session and is refused) and run `{EngineCommand.Name} {command}` from the project root", handOff, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("approve tasks")]
    [InlineData("baseline tests")]
    [InlineData("redgreen reset")]
    public void AtTheNativeLevelAHandOffNamesOnlyTheTerminal(string command)
    {
        NativeLevelSettings.Write(root.FullName, pluginEnabled: false);

        string handOff = HumanOnlyCommands.AskTheUser(command, root.FullName);

        Assert.StartsWith("ask the user to open a separate terminal window", handOff, StringComparison.Ordinal);
        Assert.DoesNotContain("/lockflight:", handOff, StringComparison.Ordinal);
    }

    [Fact]
    public void AtTheNativeLevelTheRefusalNamesNoSessionCommand()
    {
        NativeLevelSettings.Write(root.FullName, pluginEnabled: false);

        Assert.DoesNotContain("/lockflight:", HumanOnlyCommands.Refusal(["approve", "spec"], root.FullName, EngineCommand.Linked), StringComparison.Ordinal);
    }

    [Fact]
    public void NativeHooksWithThePluginEnabledKeepTheSessionCommand()
    {
        NativeLevelSettings.Write(root.FullName, pluginEnabled: true);

        Assert.Contains("type /lockflight:reset in Claude Code", HumanOnlyCommands.AskTheUser("redgreen reset", root.FullName), StringComparison.Ordinal);
    }

    public void Dispose() => root.Delete(recursive: true);
}
