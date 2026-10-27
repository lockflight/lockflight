using Lockflight.Cli.Hooks;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Hosting;

public sealed class HumanOnlyCommandsTests
{
    [Theory]
    [InlineData("dotnet lockflight baseline build", "baseline build")]
    [InlineData("lockflight  baseline tests", "baseline tests")]
    [InlineData("lockflight --verbose approve spec", "approve spec")]
    [InlineData(".lockflight/bin/lockflight.exe approve tasks --sign", "approve tasks")]
    [InlineData("cd src && \"/opt/tools/lockflight\" redgreen reset", "redgreen reset")]
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
    [InlineData("dotnet lockflight approve verify --allowed-signers signers")]
    [InlineData("lockflight redgreen red")]
    [InlineData("lockflight review --base main")]
    [InlineData("lockflight review status --feature 001-orders")]
    [InlineData("lockflight gate run --profile pr")]
    [InlineData("echo approve && lockflight phase check tasks")]
    public void OtherCommandsPass(string command) => Assert.Null(HumanOnlyCommands.FindIn(command, ShellDialect.Posix));

    [Fact]
    public void TheRefusalSaysToUseASeparateTerminalAndNotTheBangPrefix()
    {
        string refusal = HumanOnlyCommands.Refusal(["approve", "spec", "--feature", "001-orders"], "/work/shop", EngineCommand.Linked);

        Assert.Contains("'approve spec' records a human decision", refusal, StringComparison.Ordinal);
        Assert.Contains("separate terminal window", refusal, StringComparison.Ordinal);
        Assert.Contains("not with `!` in Claude Code", refusal, StringComparison.Ordinal);
        Assert.EndsWith("cd '/work/shop' && .lockflight/bin/lockflight approve spec --feature 001-orders", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCommandToCopyQuotesArgumentsTheShellWouldSplit() =>
        Assert.EndsWith(
            "cd '/work/my shop' && dotnet lockflight approve tasks --sign --key '/keys/it'\\''s key'",
            HumanOnlyCommands.Refusal(["approve", "tasks", "--sign", "--key", "/keys/it's key"], "/work/my shop", EngineCommand.Tool),
            StringComparison.Ordinal);
}
