using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Gates.Steps;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Reasons;

namespace Lockflight.Cli.Tests.Gates.Steps;

/// <summary>
/// The vulnerable-packages step with the .NET SDK replaced by canned answers: the .NET 10 listing command, and the older one an SDK 8
/// or 9 pinned by <c>global.json</c> understands.
/// </summary>
public sealed class DependencyStepTests : IDisposable
{
    private const string CriticalAdvisory = """
        { "version": 1, "parameters": "--vulnerable", "projects": [ { "path": "src/Shop.csproj", "frameworks": [ { "framework": "net8.0",
          "topLevelPackages": [ { "id": "Shop.Vulnerable", "resolvedVersion": "1.0.0",
            "vulnerabilities": [ { "severity": "Critical", "advisoryurl": "https://example.com/advisory" } ] } ] } ] } ] }
        """;

    private const string NoAdvisories = """{ "version": 1, "parameters": "--vulnerable", "projects": [ { "path": "src/Shop.csproj" } ] }""";

    private const string Sdk8Refusal = "Unrecognized command or argument 'list'.\nUnrecognized command or argument '--vulnerable'.";

    private readonly DirectoryInfo project = Directory.CreateTempSubdirectory("lockflight-deps-");

    private readonly List<string> calls = [];

    [Fact]
    public async Task TheDotNet10ListingIsTheOnlyCallWhenItGivesAReport()
    {
        GateStep step = await RunAsync(nounFirst: Answer(0, CriticalAdvisory), verbFirst: Answer(0, NoAdvisories));

        Assert.Equal(Verdict.Fail, step.Verdict);
        Assert.Equal(ReasonCode.VulnerablePackage, step.Reason);
        Assert.Equal(["dotnet package list --vulnerable --include-transitive --format json"], calls);
    }

    [Fact]
    public async Task AnOlderSdkWithoutThePackageCommandIsAskedWithTheListCommand()
    {
        GateStep step = await RunAsync(nounFirst: Answer(1, Sdk8Refusal), verbFirst: Answer(0, CriticalAdvisory));

        Assert.Equal(Verdict.Fail, step.Verdict);
        Assert.Contains("Shop.Vulnerable", step.Detail, StringComparison.Ordinal);
        Assert.Equal(
            ["dotnet package list --vulnerable --include-transitive --format json", "dotnet list package --vulnerable --include-transitive --format json"],
            calls);
    }

    [Fact]
    public async Task AListingThatNamesNoProjectIsAnErrorNotAPass()
    {
        GateStep step = await RunAsync(nounFirst: Answer(0, """{ "version": 1, "parameters": "--vulnerable", "projects": [] }"""), verbFirst: Answer(0, NoAdvisories));

        Assert.Equal(Verdict.Error, step.Verdict);
        Assert.Equal(ReasonCode.ToolError, step.Reason);
        Assert.Contains("names no project", step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOlderSdkWithNoAdvisoriesPasses()
    {
        GateStep step = await RunAsync(nounFirst: Answer(1, "Unhandled exception: Value cannot be null. (Parameter 'path2')"), verbFirst: Answer(0, NoAdvisories));

        Assert.Equal(Verdict.Pass, step.Verdict);
    }

    [Fact]
    public async Task TheBuildFileIsAnOptionOfThePackageCommandAndAnArgumentOfTheListCommand()
    {
        await RunAsync(nounFirst: Answer(1, Sdk8Refusal), verbFirst: Answer(0, NoAdvisories), buildFile: "Shop.sln");

        Assert.Equal(
            ["dotnet package list --project Shop.sln --vulnerable --include-transitive --format json", "dotnet list Shop.sln package --vulnerable --include-transitive --format json"],
            calls);
    }

    [Fact]
    public async Task NoReportFromEitherCommandIsAToolErrorNamingBoth()
    {
        GateStep step = await RunAsync(nounFirst: Answer(1, Sdk8Refusal), verbFirst: Answer(1, "Unrecognized command or argument '--format'"));

        Assert.Equal(Verdict.Error, step.Verdict);
        Assert.Equal(ReasonCode.ToolError, step.Reason);
        Assert.Equal("dotnet package list exited 1 and dotnet list package exited 1, both without a report", step.Detail);
    }

    [Fact]
    public async Task ATimedOutListingIsNotAskedAgain()
    {
        GateStep step = await RunAsync(nounFirst: new ProcessResult(-1, string.Empty, string.Empty, 5000, TimedOut: true), verbFirst: Answer(0, NoAdvisories));

        Assert.Equal(Verdict.Error, step.Verdict);
        Assert.Equal(ReasonCode.StepTimedOut, step.Reason);
        Assert.Single(calls);
    }

    [Fact]
    public async Task AListingWhoseOutputStaysHeldIsAToolErrorNotAVerdict()
    {
        ProcessResult held = Answer(0, "{ \"version\": 1, \"projects\": [ { \"path\"") with { OutputHeld = true };

        GateStep step = await RunAsync(nounFirst: held, verbFirst: Answer(0, NoAdvisories));

        Assert.Equal(Verdict.Error, step.Verdict);
        Assert.Equal(ReasonCode.ToolError, step.Reason);
        Assert.Contains("kept its output open", step.Detail, StringComparison.Ordinal);
        Assert.Equal(2, calls.Count);
        Assert.All(calls, call => Assert.StartsWith("dotnet package list", call, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AReportThatIsNotWholeJsonIsAToolError()
    {
        GateStep step = await RunAsync(nounFirst: Answer(0, "{ \"version\": 1, \"projects\": [ { \"path\""), verbFirst: Answer(0, NoAdvisories));

        Assert.Equal(Verdict.Error, step.Verdict);
        Assert.Equal(ReasonCode.ToolError, step.Reason);
        Assert.StartsWith("the package listing's report could not be read", step.Detail, StringComparison.Ordinal);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(project);

    private static ProcessResult Answer(int exitCode, string output) => new(exitCode, output, string.Empty, 10, TimedOut: false);

    private async Task<GateStep> RunAsync(ProcessResult nounFirst, ProcessResult verbFirst, string? buildFile = null)
    {
        GateRunContext run = new(project.FullName, project.FullName, GateProfile.PullRequest, GateSettings.Default, BaseRef: null, TimeProvider.System)
        {
            BuildFile = buildFile,
            Processes = (fileName, arguments, workingDirectory, timeLimit, cancellationToken) =>
            {
                calls.Add($"{Path.GetFileNameWithoutExtension(fileName)} {string.Join(' ', arguments)}");
                return Task.FromResult(arguments[0] == "package" ? nounFirst : verbFirst);
            },
        };
        DependencyScan scan = await DependencyStep.ScanAsync(run, TestContext.Current.CancellationToken);
        return scan.Step;
    }
}
