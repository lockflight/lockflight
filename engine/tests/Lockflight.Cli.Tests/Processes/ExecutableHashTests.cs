using System.Security.Cryptography;
using System.Text;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Processes;

public sealed class ExecutableHashTests : IDisposable
{
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("lockflight-hash-");

    private readonly DirectoryInfo copies = Directory.CreateTempSubdirectory("lockflight-hash-copies-");

    [Fact]
    public async Task TheBinaryItWasInstalledWithIsCopiedAndTheCopyRuns()
    {
        string binary = Write("tool", "the installed tool");

        VerifiedExecutable verified = await ExecutableHash.CopyAndVerifyAsync(binary, Sha256Of("the installed tool").ToUpperInvariant(), copies, TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(copies.FullName, "tool"), verified.Path);
        Assert.Equal("the installed tool", File.ReadAllText(verified.Path!));
    }

    [Fact]
    public async Task TheCopyIsTheOwnersAlone()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file modes");
        string binary = Write("tool", "the installed tool");

        VerifiedExecutable verified = await ExecutableHash.CopyAndVerifyAsync(binary, Sha256Of("the installed tool"), copies, TestContext.Current.CancellationToken);

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(verified.Path!));
        }
    }

    [Fact]
    public async Task ReplacingTheInstalledBinaryAfterTheCheckDoesNotChangeWhatRuns()
    {
        string binary = Write("tool", "the installed tool");
        VerifiedExecutable verified = await ExecutableHash.CopyAndVerifyAsync(binary, Sha256Of("the installed tool"), copies, TestContext.Current.CancellationToken);

        File.WriteAllText(binary, "#!/bin/sh\nexit 0\n");

        Assert.Equal("the installed tool", File.ReadAllText(verified.Path!));
    }

    [Fact]
    public async Task AReplacedBinaryIsRefused()
    {
        string binary = Write("tool", "the installed tool");
        string installed = Sha256Of("the installed tool");
        File.WriteAllText(binary, "#!/bin/sh\nexit 0\n");

        VerifiedExecutable verified = await ExecutableHash.CopyAndVerifyAsync(binary, installed, copies, TestContext.Current.CancellationToken);

        Assert.Null(verified.Path);
        Assert.Null(verified.UnreadableWhy);
        Assert.Contains(installed, verified.ReplacedWhy, StringComparison.Ordinal);
        Assert.Contains(Sha256Of("#!/bin/sh\nexit 0\n"), verified.ReplacedWhy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingBinaryIsUnreadableNotReplaced()
    {
        VerifiedExecutable verified = await ExecutableHash.CopyAndVerifyAsync(Path.Combine(directory.FullName, "gone"), Sha256Of("x"), copies, TestContext.Current.CancellationToken);

        Assert.Null(verified.Path);
        Assert.Null(verified.ReplacedWhy);
        Assert.Contains("could not be copied and read", verified.UnreadableWhy, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        TempDirectories.DeleteRecursively(directory);
        TempDirectories.DeleteRecursively(copies);
    }

    private static string Sha256Of(string content) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private string Write(string name, string content)
    {
        string path = Path.Combine(directory.FullName, name);
        File.WriteAllText(path, content);
        return path;
    }
}
