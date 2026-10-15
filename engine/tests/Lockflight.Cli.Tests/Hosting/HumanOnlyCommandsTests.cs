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
    public void ReservedCommandsAreFoundWhateverTheSpelling(string command, string expected) =>
        Assert.Equal(expected, HumanOnlyCommands.FindIn(command, ShellDialect.Posix));

    [Theory]
    [InlineData("dotnet lockflight approve verify --allowed-signers signers")]
    [InlineData("lockflight redgreen red")]
    [InlineData("lockflight gate run --profile pr")]
    [InlineData("echo approve && lockflight phase check tasks")]
    public void OtherCommandsPass(string command) => Assert.Null(HumanOnlyCommands.FindIn(command, ShellDialect.Posix));
}
