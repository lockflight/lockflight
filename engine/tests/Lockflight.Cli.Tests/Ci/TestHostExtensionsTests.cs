using Lockflight.Cli.Ci;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Ci;

public sealed class TestHostExtensionsTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-host-extensions-");

    [Fact]
    public void RunSettingsEntriesVSTestPropertiesAndExtensionAssembliesAreFindings()
    {
        string settings = Write("tests/Shop.Tests/test.runsettings", """<RunSettings><LoggerRunSettings><Loggers><Logger friendlyName="QuietPass" /></Loggers></LoggerRunSettings></RunSettings>""");
        TestApp app = App("tests/Shop.Tests", "Shop.Tests") with
        {
            RunSettingsFile = settings,
            VSTestProperties = new Dictionary<string, string>(StringComparer.Ordinal) { ["VSTestCollect"] = "ResultRewriter" },
        };
        Write("tests/Shop.Tests/bin/Shop.Forge.TestLogger.dll", "x");
        Write("tests/Shop.Tests/bin/Shop.ResultCollector.dll", "x");

        IReadOnlyList<TamperingFinding> findings = TestHostExtensions.Find([app], root.FullName, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.Equal(
            [
                "runsettings|runsettings-logger|tests/Shop.Tests/test.runsettings|QuietPass",
                "msbuild|vstest-property|VSTestCollect|ResultRewriter",
                "test-output|test-extension-assembly|test output|Shop.Forge.TestLogger.dll",
                "test-output|test-extension-assembly|test output|Shop.ResultCollector.dll",
            ],
            findings.Select(finding => finding.Id));
    }

    [Fact]
    public void TheSupportedFrameworksOwnAdaptersAreNotFindingsUnlessTheRepositoryBuildsThem()
    {
        TestApp app = App("tests/Shop.Tests", "Shop.Tests");
        Write("tests/Shop.Tests/bin/xunit.runner.visualstudio.testadapter.dll", "x");
        Write("tests/Shop.Tests/bin/coverlet.collector.dll", "x");

        Assert.Empty(TestHostExtensions.Find([app], root.FullName, new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
        Assert.Equal(
            ["test-output|test-extension-assembly|test output|coverlet.collector.dll"],
            TestHostExtensions.Find([app], root.FullName, new HashSet<string>(["coverlet.collector"], StringComparer.OrdinalIgnoreCase)).Select(finding => finding.Id));
    }

    [Fact]
    public void TwoTestProjectsWithTheSameExtensionsShareTheirFindings()
    {
        Write("tests/A.Tests/bin/Shop.Forge.TestAdapter.dll", "x");
        Write("tests/B.Tests/bin/Shop.Forge.TestAdapter.dll", "x");

        IReadOnlyList<TamperingFinding> findings = TestHostExtensions.Find(
            [App("tests/A.Tests", "A.Tests"), App("tests/B.Tests", "B.Tests")], root.FullName, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.Single(findings);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private TestApp App(string projectDirectory, string assemblyName) =>
        new(Path.Combine(root.FullName, projectDirectory), Path.Combine(root.FullName, projectDirectory, "bin", assemblyName + ".dll"));

    private string Write(string relativePath, string content)
    {
        string path = Path.Combine(root.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }
}
