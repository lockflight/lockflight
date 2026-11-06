using System.Security.Cryptography;
using System.Text;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Processes;

public sealed class ExecutableHashTests : IDisposable
{
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("lockflight-hash-");

    [Fact]
    public async Task TheBinaryItWasInstalledWithPasses()
    {
        string binary = Write("tool", "the installed tool");

        Assert.Null(await ExecutableHash.MismatchAsync(binary, Sha256Of("the installed tool").ToUpperInvariant(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AReplacedBinaryIsRefused()
    {
        string binary = Write("tool", "the installed tool");
        string installed = Sha256Of("the installed tool");
        File.WriteAllText(binary, "#!/bin/sh\nexit 0\n");

        string? why = await ExecutableHash.MismatchAsync(binary, installed, TestContext.Current.CancellationToken);

        Assert.NotNull(why);
        Assert.Contains(installed, why, StringComparison.Ordinal);
        Assert.Contains(Sha256Of("#!/bin/sh\nexit 0\n"), why, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingBinaryIsRefused()
    {
        string? why = await ExecutableHash.MismatchAsync(Path.Combine(directory.FullName, "gone"), Sha256Of("x"), TestContext.Current.CancellationToken);

        Assert.NotNull(why);
        Assert.Contains("could not be read", why, StringComparison.Ordinal);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(directory);

    private static string Sha256Of(string content) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private string Write(string name, string content)
    {
        string path = Path.Combine(directory.FullName, name);
        File.WriteAllText(path, content);
        return path;
    }
}
