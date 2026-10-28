using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Testing;

public sealed class UnevaluatedProjectsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "shop");

    private static readonly TimeSpan TimeLimit = TimeSpan.FromSeconds(600);

    [Fact]
    public void AFailedEvaluationNamesTheProjectItsErrorAndHowToFixIt()
    {
        string message = UnevaluatedProjects.Describe(Root, [Failed("tools/Broken/Broken.csproj", "error MSB4019: The imported project was not found")], TimeLimit);

        Assert.Contains("tools/Broken/Broken.csproj (evaluation failed: error MSB4019: The imported project was not found)", message, StringComparison.Ordinal);
        Assert.Contains("dotnet msbuild <project> -getProperty:IsTestProject", message, StringComparison.Ordinal);
        Assert.Contains("no setting excludes a project", message, StringComparison.Ordinal);
        Assert.DoesNotContain("gates.timeoutSeconds", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATimedOutEvaluationPointsAtTheTimeLimitSetting()
    {
        string message = UnevaluatedProjects.Describe(Root, [new UnevaluatedProject(Path.Combine(Root, "tests", "Slow", "Slow.csproj"), ProjectEvaluationFailure.TimedOut, string.Empty)], TimeLimit);

        Assert.Contains("tests/Slow/Slow.csproj (evaluation timed out)", message, StringComparison.Ordinal);
        Assert.Contains("gates.timeoutSeconds (600 s", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ManyUnevaluatedProjectsAreCountedAfterTheFirstFew()
    {
        string message = UnevaluatedProjects.Describe(Root, [.. Enumerable.Range(1, 5).Select(number => Failed($"p{number}/P{number}.csproj", "error"))], TimeLimit);

        Assert.Contains("5 projects could not be evaluated", message, StringComparison.Ordinal);
        Assert.Contains("p3/P3.csproj", message, StringComparison.Ordinal);
        Assert.DoesNotContain("p4/P4.csproj", message, StringComparison.Ordinal);
        Assert.Contains("and 2 more", message, StringComparison.Ordinal);
    }

    private static UnevaluatedProject Failed(string relativePath, string detail) =>
        new(Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar)), ProjectEvaluationFailure.Failed, detail);
}
