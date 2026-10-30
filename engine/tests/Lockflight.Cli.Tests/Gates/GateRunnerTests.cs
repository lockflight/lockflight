using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Gates;

/// <summary>The single-repository runner over the step table, with the .NET SDK replaced by canned results.</summary>
public sealed class GateRunnerTests : IDisposable
{
    private const string PassingTrx = """
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results><UnitTestResult testName="Shop.Tests.OrderTotal" outcome="Passed" /></Results>
          <ResultSummary outcome="Completed"><Counters total="1" executed="1" passed="1" failed="0" /></ResultSummary>
        </TestRun>
        """;

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-gate-runner-");

    private readonly List<string> calls = [];

    [Theory]
    [InlineData("edit", "format")]
    [InlineData("stop", "format,projects.affected,build,test")]
    [InlineData("pr", "policy.protected,format,quality.suppressions,docs.references,build,ef.pendingModelChanges,test,coverage.patch,security.deps,security.secrets")]
    public async Task EachProfileRunsItsStepsInTheTablesOrder(string profileName, string expected)
    {
        Assert.True(GateProfileNames.TryParse(profileName, out GateProfile profile));

        GateResult result = await RunAsync(profile, failing: null);

        Assert.Equal(expected, string.Join(',', result.Steps.Select(step => step.Name)));
        Assert.Equal(expected, string.Join(',', GateProfiles.For(profile).InOrder.Select(step => step.Name)));
    }

    [Fact]
    public async Task TheRunStopsAtTheFirstStepThatDoesNotPassAndTakesItsVerdict()
    {
        GateResult result = await RunAsync(GateProfile.PullRequest, failing: "build");

        Assert.Equal("policy.protected,format,quality.suppressions,docs.references,build", string.Join(',', result.Steps.Select(step => step.Name)));
        Assert.Equal(Verdict.Fail, result.Verdict);
        Assert.DoesNotContain(calls, call => call.StartsWith("dotnet test", StringComparison.Ordinal));
        Assert.Null(result.TestsTotal);
    }

    [Fact]
    public async Task TheTestCountComesFromTheTestStep()
    {
        GateResult result = await RunAsync(GateProfile.Stop, failing: null);

        Assert.Equal(1, result.TestsTotal);
        Assert.Equal(["dotnet format", "dotnet build", "dotnet test"], calls.Select(call => string.Join(' ', call.Split(' ').Take(2))));
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private async Task<GateResult> RunAsync(GateProfile profile, string? failing)
    {
        // The suppressions step asks git what changed: an empty repository has no changes.
        await ProcessRunner.RunAsync("git", ["init", "-q"], root.FullName, TestContext.Current.CancellationToken);
        GateRunContext run = new(root.FullName, root.FullName, profile, GateSettings.Default, BaseRef: null, TimeProvider.System)
        {
            Processes = (fileName, arguments, workingDirectory, timeLimit, cancellationToken) => FakeToolAsync(fileName, arguments, failing),
        };
        return await GateRunner.RunAsync(run, TextWriter.Null, TestContext.Current.CancellationToken);
    }

    private Task<ProcessResult> FakeToolAsync(string fileName, IReadOnlyList<string> arguments, string? failing)
    {
        calls.Add($"{Path.GetFileNameWithoutExtension(fileName)} {string.Join(' ', arguments)}");
        string verb = arguments[0];
        if (verb == failing)
        {
            return Task.FromResult(new ProcessResult(1, "Program.cs(1,1): error CS1002: ; expected", string.Empty, 0, TimedOut: false));
        }

        if (verb == "test")
        {
            string results = arguments[arguments.ToList().IndexOf("--results-directory") + 1];
            Directory.CreateDirectory(results);
            File.WriteAllText(Path.Combine(results, "lockflight.trx"), PassingTrx);
        }

        string output = verb == "package" ? """{ "version": 1, "parameters": "", "projects": [] }""" : string.Empty;
        return Task.FromResult(new ProcessResult(0, output, string.Empty, 0, TimedOut: false));
    }
}
