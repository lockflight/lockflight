using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Tests.Paths;

public sealed class WindowsPathsTests
{
    [Theory]
    [InlineData(@"C:\Users\RUNNER~1\AppData", true)]
    [InlineData(@"C:\PROGRA~1\app.exe", true)]
    [InlineData(@"C:\Users\runneradmin\AppData", false)]
    [InlineData("/home/user/src/a.cs", false)]
    public void EightDotThreeAliasesAreRecognised(string path, bool expected) =>
        Assert.Equal(expected, WindowsPaths.HasShortNameSegment(path));
}
