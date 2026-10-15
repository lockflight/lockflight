using Lockflight.Cli.Gates;

namespace Lockflight.Cli.Tests.Gates;

public sealed class TestRunnerDetectionTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-runner-");

    [Fact]
    public void WithoutAGlobalJsonDotnetTestRunsVSTest()
    {
        Assert.Equal(TestRunnerKind.VSTest, TestRunnerDetection.For(Child("services/orders")));
    }

    [Fact]
    public void TheNearestGlobalJsonChoosesTheTestingPlatform()
    {
        File.WriteAllText(Path.Combine(root.FullName, "global.json"), """{ "sdk": { "version": "10.0.100" } }""");
        string billing = Child("services/billing");
        File.WriteAllText(Path.Combine(billing, "global.json"), """
            {
              // the SDK allows comments
              "test": { "runner": "Microsoft.Testing.Platform" },
            }
            """);

        Assert.Equal(TestRunnerKind.MicrosoftTestingPlatform, TestRunnerDetection.For(Path.Combine(billing, "tests")));
        Assert.Equal(TestRunnerKind.VSTest, TestRunnerDetection.For(Child("services/orders")));
    }

    [Fact]
    public void AnUnreadableGlobalJsonMeansVSTest()
    {
        File.WriteAllText(Path.Combine(root.FullName, "global.json"), "{ not json");

        Assert.Equal(TestRunnerKind.VSTest, TestRunnerDetection.For(root.FullName));
    }

    private string Child(string relative)
    {
        string path = Path.Combine(root.FullName, relative);
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);
}
