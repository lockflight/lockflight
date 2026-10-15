using System.Diagnostics;
using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Hooks;

[Collection(SerialPerformanceTests.Name)]
public sealed class GuardPerformanceTests : IDisposable
{
    private const int PackageCount = 1500;
    private const int MaxDecisionMilliseconds = 200;
    private const int MaxTimesOneWalk = 4;
    private const int TimedRuns = 3;

    private readonly DirectoryInfo projectRoot = Directory.CreateTempSubdirectory("lockflight-perf-");

    public GuardPerformanceTests()
    {
        File.WriteAllText(Path.Combine(projectRoot.FullName, Manifest.FileName), "protected:\n  - secrets/**\n");
        for (int index = 0; index < PackageCount; index++)
        {
            string package = Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "node_modules", $"p{index}", "lib")).FullName;
            File.WriteAllText(Path.Combine(package, "index.js"), string.Empty);
        }
    }

    [Theory]
    [InlineData("rm -rf node_modules/*")]
    [InlineData("rm -rf node_modules/p1*")]
    [InlineData("rm -rf node_modules/*/lib")]
    public async Task AWildcardOverThousandsOfDirectoriesIsDecidedQuickly(string command)
    {
        // Slow CI runners get the allowance of a few walks of the whole directory (what "rm -rf node_modules" costs anyway).
        long oneWalk = await TimeAsync("rm -rf node_modules");
        long elapsed = await TimeAsync(command);

        Assert.InRange(elapsed, 0, Math.Max(MaxDecisionMilliseconds, MaxTimesOneWalk * oneWalk));
    }

    /// <summary>The best of a few timed runs after a warm-up: one run on a shared CI runner is too noisy to hold to a bound.</summary>
    private async Task<long> TimeAsync(string command)
    {
        await RunAsync(command);
        long best = long.MaxValue;
        for (int run = 0; run < TimedRuns; run++)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            (ExitCode exitCode, _, string error) = await RunAsync(command);
            stopwatch.Stop();
            Assert.True(exitCode == ExitCode.Pass, error);
            best = Math.Min(best, stopwatch.ElapsedMilliseconds);
        }

        return best;
    }

    private Task<(ExitCode ExitCode, string Output, string Error)> RunAsync(string command)
    {
        string payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["cwd"] = projectRoot.FullName,
            ["tool_name"] = "Bash",
            ["tool_input"] = new Dictionary<string, string> { ["command"] = command },
        });
        return TestConsole.RunAsync(["hook", "pre-tool-use"], payload, projectRoot.FullName);
    }

    public void Dispose() => projectRoot.Delete(recursive: true);
}
