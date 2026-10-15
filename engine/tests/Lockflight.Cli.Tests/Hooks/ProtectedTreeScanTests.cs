using Lockflight.Cli.Config;
using Lockflight.Cli.Hooks;

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

    [Fact]
    public void APrimedParentServesItsChildren()
    {
        File.WriteAllText(Path.Combine(projectRoot.FullName, "node_modules", "p1", "Directory.Build.props"), "<Project />");
        ProtectedTreeScan scan = Scan();
        scan.Prime("node_modules", siblingCount: 10);

        Assert.True(scan.CouldMatchUnder("node_modules/p1", BuildProps, sourceOnly: false));
        Assert.False(scan.CouldMatchUnder("node_modules/p2", BuildProps, sourceOnly: false));
    }

    private ProtectedTreeScan Scan() => new(projectRoot.FullName, DefaultProtectedPaths.With(["secrets/**"]));

    public void Dispose() => projectRoot.Delete(recursive: true);
}
