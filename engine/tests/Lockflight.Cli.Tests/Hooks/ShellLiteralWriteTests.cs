using Lockflight.Cli.Hooks;

namespace Lockflight.Cli.Tests.Hooks;

public sealed class ShellLiteralWriteTests
{
    [Theory]
    [InlineData("echo x > ../../../../../../../../dev/tty")]
    [InlineData("echo x > ../../../../../../../../proc/self/fd/1")]
    [InlineData("echo x > ../../../../../../../../.claude/loop.md")]
    [InlineData("echo x > D:/dev/tty")]
    public void ADriveRootedWorkingDirectoryStopsAtTheDriveRoot(string command) =>
        Assert.False(ShellLiteralWrite.OnlyWritesLiteralText(command, @"D:\a\project\src"));

    [Theory]
    [InlineData("echo x > notes.md")]
    [InlineData("echo x > ../docs/notes.md")]
    [InlineData("echo x > /dev/null")]
    [InlineData("echo x > D:/a/project/notes.md")]
    public void APlainFileUnderADriveRootedWorkingDirectoryIsALiteralWrite(string command) =>
        Assert.True(ShellLiteralWrite.OnlyWritesLiteralText(command, @"D:\a\project\src"));
}
