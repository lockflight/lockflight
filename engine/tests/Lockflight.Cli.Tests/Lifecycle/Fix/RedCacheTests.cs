using Lockflight.Cli.Ci;
using Lockflight.Cli.Lifecycle.Fix;
using Lockflight.Cli.Lifecycle.RedGreen;

namespace Lockflight.Cli.Tests.Lifecycle.Fix;

public sealed class RedCacheTests : IDisposable
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("lockflight-red-cache-");

    [Fact]
    public void AForkInventoryAndARedReadBackAsWritten()
    {
        RedCache cache = new(directory.FullName, "policy", "engine", "10.0.100");
        ForkInventory fork = new(Keys("a", "b"), Keys("b"), Keys());
        cache.WriteFork(Commit, fork);
        cache.WriteRed(Commit, new CachedRed(Accepted: true, [new RedGreenTest("k", "Shop.Tests.PriceTests.M", RedClass.RedAssert, [])]));

        ForkInventory? readFork = cache.ReadFork(Commit);
        CachedRed? readRed = cache.ReadRed(Commit);

        Assert.NotNull(readFork);
        Assert.Equal(["a", "b"], readFork.Tests.Order(StringComparer.Ordinal));
        Assert.Equal(["b"], readFork.Skipped);
        Assert.NotNull(readRed);
        Assert.True(readRed.Accepted);
        Assert.Equal(RedClass.RedAssert, Assert.Single(readRed.Tests).RedClass);
    }

    [Theory]
    [InlineData("other policy", "engine", "10.0.100")]
    [InlineData("policy", "other engine", "10.0.100")]
    [InlineData("policy", "engine", "10.0.200")]
    public void AnEntryMissesWhenThePolicyTheEngineOrTheSdkDiffers(string policy, string engine, string sdk)
    {
        new RedCache(directory.FullName, "policy", "engine", "10.0.100").WriteFork(Commit, new ForkInventory(Keys("a"), Keys(), Keys()));

        Assert.Null(new RedCache(directory.FullName, policy, engine, sdk).ReadFork(Commit));
    }

    [Fact]
    public void AnEntryThatIsNotWhatTheCacheWroteIsAMiss()
    {
        RedCache cache = new(directory.FullName, "policy", "engine", "10.0.100");
        cache.WriteRed(Commit, new CachedRed(Accepted: false, []));
        foreach (string file in Directory.EnumerateFiles(directory.FullName))
        {
            File.WriteAllText(file, "{ \"accepted\": \"yes\", \"tests\": [ { \"key\": 1 } ] }");
        }

        Assert.Null(cache.ReadRed(Commit));
    }

    public void Dispose() => TempDirectories.DeleteRecursively(directory);

    private static HashSet<string> Keys(params string[] keys) => new(keys, StringComparer.Ordinal);
}
