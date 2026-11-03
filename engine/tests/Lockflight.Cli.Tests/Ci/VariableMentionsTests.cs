using Lockflight.Cli.Ci;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Ci;

public sealed class VariableMentionsTests : IDisposable
{
    private const string Variable = "PRICES_" + "SNAPSHOTS";
    private const string TestsDirectory = "tests/Shop.Tests";

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-mentions-");

    [Fact]
    public async Task AVariableOnlyTheTestProjectsOwnCodeNamesIsNotNamedOutsideIt()
    {
        Write("tests/Shop.Tests/Shop.Tests.csproj", "<Project />");
        Write("tests/Shop.Tests/Snapshots.cs", $"Environment.GetEnvironmentVariable(\"{Variable}\")");
        Write(".gitignore", "ignored/\n");
        Write("ignored/notes.txt", Variable);

        Assert.False((await MentionsAsync()).IsNamedOutside(Variable, "Shop.Tests"));
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("docs/snapshots.md")]
    [InlineData("tests/Shop.TestKit/Env.cs")]
    [InlineData("tests/Shop.Tests/test.runsettings")]
    [InlineData("Directory.Build.props")]
    [InlineData("tests/Shop.Tests/Properties/launchSettings.json")]
    [InlineData("src/Shop/Startup.cs")]
    [InlineData(".github/workflows/ci.yml")]
    [InlineData("node_modules/kit/env.props")]
    [InlineData("tests/Shop.Tests/bin/env.targets")]
    public async Task AVariableAnyOtherFileNamesIsNamedOutsideTheTestProject(string path)
    {
        Write("tests/Shop.Tests/Shop.Tests.csproj", "<Project />");
        Write("tests/Shop.TestKit/Shop.TestKit.csproj", "<Project />");
        Write(path, $"<{Variable}>record</{Variable}>");

        Assert.True((await MentionsAsync()).IsNamedOutside(Variable, "Shop.Tests"));
    }

    [Fact]
    public async Task AFileThatCannotBeReadCountsAsNamingTheVariable()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating a symbolic link needs a privilege on Windows.");
        Write("tests/Shop.Tests/Shop.Tests.csproj", "<Project />");
        File.CreateSymbolicLink(Path.Combine(root.FullName, "dangling.runsettings"), Path.Combine(root.FullName, "missing"));

        Assert.True((await MentionsAsync()).IsNamedOutside(Variable, "Shop.Tests"));
    }

    [Fact]
    public async Task AFolderThatIsNotAGitRepositoryCountsAsNamingEveryVariable() =>
        Assert.True((await VariableMentions.ListAsync(root.FullName, new Dictionary<string, string>(), TestContext.Current.CancellationToken)).IsNamedOutside(Variable, "Shop.Tests"));

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private async Task<VariableMentions> MentionsAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await ProcessRunner.RunAsync("git", ["init", "-q"], root.FullName, cancellationToken);
        await ProcessRunner.RunAsync("git", ["add", "-A", "-f", "--", ".", ":(exclude)ignored"], root.FullName, cancellationToken);
        Dictionary<string, string> projects = new(StringComparer.OrdinalIgnoreCase) { ["Shop.Tests"] = Path.Combine(root.FullName, TestsDirectory) };
        return await VariableMentions.ListAsync(root.FullName, projects, cancellationToken);
    }

    private void Write(string path, string content)
    {
        string full = Path.Combine(root.FullName, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }
}
