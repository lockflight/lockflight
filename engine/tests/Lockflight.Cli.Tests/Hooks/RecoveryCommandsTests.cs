using Lockflight.Cli.Hooks;

namespace Lockflight.Cli.Tests.Hooks;

/// <summary>The user's install is named by its absolute path under the hook's HOME, quoted as the plugin shim quotes it.</summary>
public sealed class RecoveryCommandsTests
{
    [Theory]
    [InlineData("/home/user", "/home/user/.lockflight/bin/lockflight link --installed")]
    [InlineData("/home/user/", "/home/user/.lockflight/bin/lockflight link --installed")]
    [InlineData("/Users/x y", "'/Users/x y/.lockflight/bin/lockflight' link --installed")]
    [InlineData("C:\\Users\\dev", "'C:\\Users\\dev/.lockflight/bin/lockflight' link --installed")]
    [InlineData("/c/Users/user", "/c/Users/user/.lockflight/bin/lockflight link --installed")]
    [InlineData("/home/\u00e9", "'/home/\u00e9/.lockflight/bin/lockflight' link --installed")]
    public void TheLinkInstalledCommandNamesTheInstallByItsAbsolutePath(string home, string expected) =>
        Assert.Equal(expected, RecoveryCommands.LinkInstalled(home));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/home/u'x")]
    [InlineData("relative")]
    [InlineData("~")]
    public void WithoutAnAbsoluteHomeASingleQuoteCanHoldThereIsNoLinkInstalledCommand(string? home) =>
        Assert.Null(RecoveryCommands.LinkInstalled(home));

    [Fact]
    public void TheTildeFormIsNeverAllowed() =>
        Assert.DoesNotContain("~/.lockflight/bin/lockflight link --installed", RecoveryCommands.Commands("/home/user"));
}
