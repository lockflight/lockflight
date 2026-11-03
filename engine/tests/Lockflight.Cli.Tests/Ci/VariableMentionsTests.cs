using Lockflight.Cli.Ci;

namespace Lockflight.Cli.Tests.Ci;

public sealed class VariableMentionsTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-mentions-");

    [Fact]
    public void AVariableOnlyTheTestCodeAndTheDocsNameIsNotNamedOutsideTheTests()
    {
        Write("tests/Shop.Tests/Shop.Tests.csproj", "<Project />");
        Write("tests/Shop.Tests/Snapshots.cs", "Environment.GetEnvironmentVariable(\"PRICES_SNAPSHOTS\")");
        Write("README.md", "Set PRICES_SNAPSHOTS=record to record.");
        Write("tests/Shop.Tests/bin/Debug/Shop.Tests.deps.json", "PRICES_SNAPSHOTS");

        Assert.False(Mentions().IsNamedOutsideTheTests("PRICES_SNAPSHOTS"));
    }

    [Theory]
    [InlineData("tests/Shop.Tests/test.runsettings", "<EnvironmentVariables><PRICES_SNAPSHOTS>record</PRICES_SNAPSHOTS></EnvironmentVariables>")]
    [InlineData("Directory.Build.props", "<Project><PropertyGroup><RunSettingsFilePath>x</RunSettingsFilePath></PropertyGroup><!-- prices_snapshots --></Project>")]
    [InlineData("tests/Shop.Tests/Properties/launchSettings.json", "{ \"environmentVariables\": { \"PRICES_SNAPSHOTS\": \"record\" } }")]
    [InlineData("src/Shop/Startup.cs", "Environment.SetEnvironmentVariable(\"PRICES_SNAPSHOTS\", \"record\");")]
    [InlineData(".github/workflows/ci.yml", "env:\n  PRICES_SNAPSHOTS: record\n")]
    public void AVariableAnyOtherFileNamesIsNamedOutsideTheTests(string path, string content)
    {
        Write("tests/Shop.Tests/Shop.Tests.csproj", "<Project />");
        Write("src/Shop/Shop.csproj", "<Project />");
        Write(path, content);

        Assert.True(Mentions().IsNamedOutsideTheTests("PRICES_SNAPSHOTS"));
    }

    [Fact]
    public void AFileThatCannotBeReadCountsAsNamingTheVariable()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating a symbolic link needs a privilege on Windows.");
        Write("tests/Shop.Tests/Shop.Tests.csproj", "<Project />");
        File.CreateSymbolicLink(Path.Combine(root.FullName, "dangling.runsettings"), Path.Combine(root.FullName, "missing"));

        Assert.True(Mentions().IsNamedOutsideTheTests("PRICES_SNAPSHOTS"));
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private VariableMentions Mentions() => new(root.FullName, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Shop" });

    private void Write(string path, string content)
    {
        string full = Path.Combine(root.FullName, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }
}
