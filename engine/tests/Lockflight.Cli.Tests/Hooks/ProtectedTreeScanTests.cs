using Lockflight.Cli.Config;
using Lockflight.Cli.Hooks;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Tests.Hooks;

public sealed class ProtectedTreeScanTests : IDisposable
{
    private const string BuildProps = "**/Directory.Build.props";

    private readonly DirectoryInfo projectRoot = Directory.CreateTempSubdirectory("lockflight-scan-");

    public ProtectedTreeScanTests()
    {
        for (int index = 0; index < 10; index++)
        {
            string package = Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "node_modules", $"p{index}", "lib")).FullName;
            File.WriteAllText(Path.Combine(package, "index.js"), string.Empty);
        }
    }

    [Theory]
    [InlineData("secrets/**")]
    [InlineData("lockflight.yaml")]
    [InlineData(BuildProps)]
    public void ATreeWithoutProtectedFilesDoesNotMatch(string glob) =>
        Assert.False(Scan().CouldMatchUnder("node_modules", glob, sourceOnly: false));

    [Fact]
    public void AProtectedFileDeepInsideATreeIsFound()
    {
        File.WriteAllText(Path.Combine(projectRoot.FullName, "node_modules", "p3", "lib", "Directory.Build.props"), "<Project />");

        Assert.True(Scan().CouldMatchUnder("node_modules", BuildProps, sourceOnly: false));
        Assert.True(Scan().CouldMatchUnder("node_modules/p3/lib", BuildProps, sourceOnly: false));
        Assert.False(Scan().CouldMatchUnder("node_modules/p4", BuildProps, sourceOnly: false));
    }

    [Theory]
    [InlineData("node_modules/p1/*", false, true)]
    [InlineData("node_modules/p1/*.props", false, true)]
    [InlineData("node_modules/p1/*.json", false, false)]
    [InlineData("node_modules/*", true, true)]
    [InlineData("node_modules/*", false, false)]
    [InlineData("node_modules/p2/*", true, false)]
    public void AWildcardIsJudgedByTheProtectedFilesItMatches(string pattern, bool asTree, bool matches)
    {
        File.WriteAllText(Path.Combine(projectRoot.FullName, "node_modules", "p1", "Directory.Build.props"), "<Project />");

        string? glob = Scan().FirstMatchOfPattern("node_modules", pattern, [BuildProps], asTree, (_, _) => false);

        Assert.Equal(matches ? BuildProps : null, glob);
    }

    private ProtectedTreeScan Scan() => new(projectRoot.FullName, DefaultProtectedPaths.With(["secrets/**"]), new DirectoryReads());

    public void Dispose() => projectRoot.Delete(recursive: true);
}
