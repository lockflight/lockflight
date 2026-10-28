using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Testing;

public sealed class TestAppsTests : IDisposable
{
    private static readonly TimeSpan TimeLimit = TimeSpan.FromSeconds(120);

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-testapps-");

    [Theory]
    [InlineData("net8.0;net10.0", null, "net10.0")]
    [InlineData("net8.0;net10.0", "net8.0", "net8.0")]
    [InlineData("net8.0;net10.0", "net9.0", "net10.0")]
    [InlineData("net472;net8.0", null, "net8.0")]
    [InlineData("netstandard2.0", null, null)]
    public void MultiTargetedTestProjectsRunOneFramework(string frameworks, string? preferred, string? expected) =>
        Assert.Equal(expected, TestApps.ChooseFramework(frameworks, preferred));

    [Fact]
    public async Task AProjectWhoseEvaluationFailsIsReportedWithItsFirstErrorLine()
    {
        WriteProject("tools/Broken/Broken.csproj", """<Import Project="missing.props" />""");

        TestAppDiscovery discovery = await TestApps.FindAsync(root.FullName, null, TimeLimit, TestContext.Current.CancellationToken);

        Assert.Empty(discovery.Apps);
        UnevaluatedProject unevaluated = Assert.Single(discovery.Unevaluated);
        Assert.Equal(Path.Combine(root.FullName, "tools", "Broken", "Broken.csproj"), unevaluated.Project);
        Assert.Equal(ProjectEvaluationFailure.Failed, unevaluated.Failure);
        Assert.Contains("MSB4019", unevaluated.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', unevaluated.Detail);
    }

    [Fact]
    public async Task AProjectThatEvaluatesAndIsNoTestAppIsNeitherAnAppNorUnevaluated()
    {
        WriteProject("src/Shop/Shop.csproj", "<PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>");

        TestAppDiscovery discovery = await TestApps.FindAsync(root.FullName, null, TimeLimit, TestContext.Current.CancellationToken);

        Assert.Empty(discovery.Apps);
        Assert.Empty(discovery.Unevaluated);
    }

    [Fact]
    public async Task AnEvaluationThatRunsOutOfTimeIsReportedAsTimedOut()
    {
        WriteProject("src/Shop/Shop.csproj", "<PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>");

        TestAppDiscovery discovery = await TestApps.FindAsync(root.FullName, null, TimeSpan.FromMilliseconds(1), TestContext.Current.CancellationToken);

        UnevaluatedProject unevaluated = Assert.Single(discovery.Unevaluated);
        Assert.Equal(ProjectEvaluationFailure.TimedOut, unevaluated.Failure);
    }

    public void Dispose() => root.Delete(recursive: true);

    private void WriteProject(string relativePath, string body)
    {
        string path = Path.Combine(root.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"<Project Sdk=\"Microsoft.NET.Sdk\">\n  {body}\n</Project>\n");
    }
}
