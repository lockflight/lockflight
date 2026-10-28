using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Lifecycle.Approvals;
using Lockflight.Cli.Lifecycle.RedGreen;

namespace Lockflight.Cli.Tests.Lifecycle;

public sealed class UnevaluatedTestProjectTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-unevaluated-");

    public UnevaluatedTestProjectTests()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");
        File.WriteAllText(Path.Combine(root.FullName, "Shop.slnx"), "<Solution />\n");
        Directory.CreateDirectory(Path.Combine(root.FullName, "tests", "Broken.Tests"));
        File.WriteAllText(
            Path.Combine(root.FullName, "tests", "Broken.Tests", "Broken.Tests.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <Import Project=\"missing.props\" />\n</Project>\n");
        Directory.CreateDirectory(Feature.SpecDirectory);
        File.WriteAllText(Feature.SpecPath, "- **FR-001**: A\n");
        File.WriteAllText(Feature.TasksPath, "- [ ] T001 [US1] (FR-001) Test A\n");
        ApprovalStore approvals = new(Feature);
        approvals.Save(ApprovalPhase.Spec, "dev@example.com", DateTimeOffset.UnixEpoch, "0.0.0-test");
        approvals.Save(ApprovalPhase.Tasks, "dev@example.com", DateTimeOffset.UnixEpoch, "0.0.0-test");
    }

    private Feature Feature => new("f1", Path.Combine(root.FullName, "specs", "f1"), Path.Combine(root.FullName, ".lockflight", "features", "f1"));

    [Theory]
    [InlineData("start", false)]
    [InlineData("red", false)]
    [InlineData("green", true)]
    public async Task RedGreenIsNotEarnedWhenATestProjectCannotBeEvaluated(string step, bool openCycle)
    {
        if (step != "start")
        {
            SaveRecord(openCycle);
        }

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["redgreen", step, "--feature", "f1"], string.Empty, root.FullName);

        Assert.Equal(ExitCode.NotEarned, exitCode);
        Assert.Contains("NOT-EARNED", output, StringComparison.Ordinal);
        Assert.Contains("tests/Broken.Tests/Broken.Tests.csproj (evaluation failed:", output, StringComparison.Ordinal);
        Assert.Contains("MSB4019", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TraceIsNotEarnedWhenATestProjectCannotBeEvaluated()
    {
        SaveRecord(openCycle: false);

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["trace", "--feature", "f1"], string.Empty, root.FullName);

        Assert.Equal(ExitCode.NotEarned, exitCode);
        Assert.Contains("NOT-EARNED", output, StringComparison.Ordinal);
        Assert.Contains("tests/Broken.Tests/Broken.Tests.csproj (evaluation failed:", output, StringComparison.Ordinal);
    }

    public void Dispose() => root.Delete(recursive: true);

    private void SaveRecord(bool openCycle)
    {
        RedGreenRecord record = new()
        {
            BaselineCommit = "abc",
            BaselineAt = DateTimeOffset.UnixEpoch,
            BaselineTests = new HashSet<string> { "T:A.B" },
            TestProjects = ["tests/Shop.Tests"],
        };
        if (openCycle)
        {
            record.Cycles.Add(new RedGreenCycle(1, DateTimeOffset.UnixEpoch, GreenAt: null, [new RedGreenTest("T:A.C", "A.C", RedClass.RedStub, ["FR-001"])]));
        }

        record.Save(RedGreenRecord.PathFor(Feature), Feature.Id);
    }
}
