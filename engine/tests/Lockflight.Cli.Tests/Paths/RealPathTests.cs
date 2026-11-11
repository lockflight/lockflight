using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Tests.Paths;

public sealed class RealPathTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-realpath-");

    [Fact]
    public void AChainOfLinksIsFollowedToItsTarget()
    {
        Assert.SkipUnless(SymbolicLinks.CanCreate, SymbolicLinks.SkipReason);
        string target = Directory.CreateDirectory(Path.Combine(root.FullName, "target")).FullName;
        Directory.CreateSymbolicLink(Path.Combine(root.FullName, "second"), target);
        Directory.CreateSymbolicLink(Path.Combine(root.FullName, "first"), Path.Combine(root.FullName, "second"));

        string resolved = RealPath.Resolve(Path.Combine(root.FullName, "first", "new.txt"));

        Assert.Equal(Normalized(Path.Combine(RealPath.Resolve(target), "new.txt")), Normalized(resolved));
    }

    [Fact]
    public void ALinkLoopIsRefusedRatherThanFollowedForever()
    {
        Assert.SkipUnless(SymbolicLinks.CanCreate, SymbolicLinks.SkipReason);
        Directory.CreateSymbolicLink(Path.Combine(root.FullName, "a"), Path.Combine(root.FullName, "b"));
        Directory.CreateSymbolicLink(Path.Combine(root.FullName, "b"), Path.Combine(root.FullName, "a"));

        IOException refused = Assert.Throws<IOException>(() => RealPath.Resolve(Path.Combine(root.FullName, "a", "x")));

        Assert.Contains("Too many levels of symbolic links", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATailThatDoesNotExistYetIsKeptAsWritten()
    {
        string resolved = RealPath.Resolve(Path.Combine(RealPath.Resolve(root.FullName), "not", "yet.txt"));

        Assert.EndsWith("/not/yet.txt", Normalized(resolved), StringComparison.Ordinal);
    }

    public void Dispose() => root.Delete(recursive: true);

    private static string Normalized(string path) => path.Replace('\\', '/');
}
