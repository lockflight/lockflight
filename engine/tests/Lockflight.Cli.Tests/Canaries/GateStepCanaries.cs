using Lockflight.Cli.Canaries;
using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Gates.Steps;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Canaries;

/// <summary>
/// Every registered gate step (<see cref="GateProfiles"/>) judged on a planted violation: its tool is replaced by one that reports
/// the violation (a compile error, a failing test, a High advisory, a leaked secret, a file the formatter would change), in a
/// scratch git repository whose last commit changes a protected file and a C# file, adding a <c>#pragma warning disable</c> the
/// suppressions step (set to fail) must not pass, and deleting the guide its <c>CLAUDE.md</c> links to, which the references step
/// (set to fail) must not pass. A step that is anything but PASS is caught;
/// the protected-changes step reports and never fails, so it is caught when it lists the protected file. A step with no planted
/// case here is MISSED, so a step added to the table without a canary shows up. The harmless twin, the build with a tool that
/// succeeds, must PASS. <see cref="FullCanaries"/> runs the real build and test tools.
/// </summary>
internal static class GateStepCanaries
{
    private const string BaseRef = "HEAD~1";
    private const string ChangedSource = "src/Order.cs";
    private const string LinkedGuide = "docs/guide.md";
    private const string LeakReportOption = "--report-path";
    private const int LeaksFoundExitCode = 99;

    private const string PlantedTrx = """
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results><UnitTestResult testName="Canary.Tests.OrderTotal" outcome="Failed" /></Results>
          <ResultSummary outcome="Failed"><Counters total="1" executed="1" passed="0" failed="1" /></ResultSummary>
        </TestRun>
        """;

    private const string PlantedAdvisory = """
        { "version": 1, "parameters": "--vulnerable", "projects": [ { "path": "src/Canary.csproj", "frameworks": [ { "framework": "net10.0",
          "topLevelPackages": [ { "id": "Canary.Vulnerable", "resolvedVersion": "1.0.0",
            "vulnerabilities": [ { "severity": "Critical", "advisoryurl": "https://example.com/advisory" } ] } ] } ] } ] }
        """;

    private const string PlantedLeak = """[ { "RuleID": "generic-api-key", "File": "src/Order.cs", "StartLine": 2, "Commit": "" } ]""";

    public static async Task<IReadOnlyList<CanaryReport>> RunAsync(TimeProvider time, CancellationToken cancellationToken)
    {
        using CanaryScratch scratch = new(time);
        if (await CreateAsync(scratch, cancellationToken) is string notCreated)
        {
            return [new CanaryReport("gate steps: a planted violation is not a PASS", CanaryResult.Broken, $"{notCreated}; git is needed for these canaries")];
        }

        Manifest manifest = ManifestLocator.Discover(scratch.Project).Manifest!;
        GateRunContext twinRun = GateRunContext.ForTarget(manifest, scratch.Project, GateProfile.PullRequest, BaseRef, time) with { Processes = SucceedingAsync };
        GateStep twin = (await new BuildStep().RunAsync(twinRun, GateRunFacts.None, cancellationToken)).Step;
        if (twin.Verdict != Verdict.Pass)
        {
            return [new CanaryReport("gate steps: a build whose tool succeeds passes", CanaryResult.Broken,
                $"it is {VerdictNames.ToContractName(twin.Verdict)} ({twin.Detail}), so a rejection proves nothing")];
        }

        List<CanaryReport> reports = [];
        foreach (IGateStep step in GateProfiles.For(GateProfile.PullRequest).InOrder)
        {
            reports.Add(await JudgeAsync(step, manifest, scratch.Project, time, cancellationToken) with { Control = step.Name });
        }

        return reports;
    }

    private static async Task<CanaryReport> JudgeAsync(IGateStep step, Manifest manifest, string project, TimeProvider time, CancellationToken cancellationToken)
    {
        GateRunContext run = GateRunContext.ForTarget(manifest, project, GateProfile.PullRequest, BaseRef, time) with { Processes = PlantedAsync };
        GateRunFacts afterTests = new(TestsTotal: 1, TestResultsDirectory: Path.Combine(project, "no-coverage-here"), TestsStarted: time.GetUtcNow());
        (string name, GateRunFacts facts) = step.Name switch
        {
            ProtectedChangesStep.StepName => ("policy: a change to lockflight.yaml since the base is listed for review", GateRunFacts.None),
            FormatStep.StepName => ("format: a changed file the formatter would change is not a PASS", GateRunFacts.None),
            BuildStep.StepName => ("build: a compile error is not a PASS", GateRunFacts.None),
            TestStep.StepName => ("test: a failing test is not a PASS", GateRunFacts.None),
            CoverageStep.StepName => ("coverage: a configured collector without a coverage report is not a PASS", afterTests),
            DependencyStep.StepName => ("dependencies: a Critical advisory is not a PASS", GateRunFacts.None),
            SecretsStep.StepName => ("secrets: a leaked secret is not a PASS", GateRunFacts.None),
            SuppressionsStep.StepName => ("suppressions: an added #pragma warning disable is not a PASS (gates.quality.suppressions: fail)", GateRunFacts.None),
            DocsReferencesStep.StepName => ("references: a CLAUDE.md link the change broke is not a PASS (gates.docs.references: fail)", GateRunFacts.None),
            _ => (string.Empty, GateRunFacts.None),
        };
        if (name.Length == 0)
        {
            return new CanaryReport($"gate step '{step.Name}': a planted violation", CanaryResult.Missed, CanaryCoverageTests.MissingGateStepCanary);
        }

        GateRunContext judged = step.Name switch
        {
            CoverageStep.StepName => run with { Settings = run.Settings with { Coverage = run.Settings.Coverage with { Collector = CoverageCollector.Coverlet } } },
            SuppressionsStep.StepName => run with { Settings = run.Settings with { Suppressions = GateMode.Fail } },
            DocsReferencesStep.StepName => run with { Settings = run.Settings with { DocsReferences = GateMode.Fail } },
            _ => run,
        };
        GateStep result = (await step.RunAsync(judged, facts, cancellationToken)).Step;
        if (step.Name == ProtectedChangesStep.StepName)
        {
            return result.Detail.Contains(Manifest.FileName, StringComparison.Ordinal)
                ? new CanaryReport(name, CanaryResult.Caught, result.Detail)
                : new CanaryReport(name, CanaryResult.Missed, $"{VerdictNames.ToContractName(result.Verdict)} without the file: {result.Detail}");
        }

        return CanaryReport.NotAPass(name, result);
    }

    /// <summary>A project whose last commit changes the manifest (a protected file) and a C# file, and deletes a guide CLAUDE.md links to.</summary>
    private static async Task<string?> CreateAsync(CanaryScratch scratch, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(scratch.PathOf(".gitignore"), ".lockflight/\n", cancellationToken);
        await File.WriteAllTextAsync(scratch.PathOf(ChangedSource), "class Order\n{\n}\n", cancellationToken);
        await File.WriteAllTextAsync(scratch.PathOf("CLAUDE.md"), $"# Canary\n\nRead [the guide]({LinkedGuide}).\n", cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(scratch.PathOf(LinkedGuide))!);
        await File.WriteAllTextAsync(scratch.PathOf(LinkedGuide), "# Guide\n", cancellationToken);
        if (await CanaryGit.CommitAllAsync(scratch.Project, "canary base", initialize: true, cancellationToken) is string baseFailed)
        {
            return baseFailed;
        }

        await File.AppendAllTextAsync(scratch.PathOf(Manifest.FileName), "protected:\n  - \"docs/**\"\n", cancellationToken);
        File.Delete(scratch.PathOf(LinkedGuide));
        await File.WriteAllTextAsync(scratch.PathOf(ChangedSource), "class Order\n{\n#pragma warning disable CS0414\n  string key = \"planted\";\n}\n", cancellationToken);
        return await CanaryGit.CommitAllAsync(scratch.Project, "canary change", initialize: false, cancellationToken);
    }

    private static Task<ProcessResult> SucceedingAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeLimit, CancellationToken cancellationToken) =>
        Task.FromResult(new ProcessResult(0, "Build succeeded.", string.Empty, 0, TimedOut: false));

    /// <summary>Each tool reports its violation: the run the steps judge as a real one.</summary>
    private static async Task<ProcessResult> PlantedAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        string tool = Path.GetFileNameWithoutExtension(fileName);
        string verb = arguments.Count > 0 ? arguments[0] : string.Empty;
        if (tool != "dotnet")
        {
            int report = arguments.ToList().IndexOf(LeakReportOption);
            if (report >= 0)
            {
                await File.WriteAllTextAsync(arguments[report + 1], PlantedLeak, cancellationToken);
            }

            return new ProcessResult(LeaksFoundExitCode, "leaks found: 1", string.Empty, 0, TimedOut: false);
        }

        switch (verb)
        {
            case "build":
                return new ProcessResult(1, "src/Order.cs(3,3): error CS1002: ; expected", string.Empty, 0, TimedOut: false);
            case "test":
                string results = arguments[arguments.ToList().IndexOf("--results-directory") + 1];
                Directory.CreateDirectory(results);
                await File.WriteAllTextAsync(Path.Combine(results, "canary.trx"), PlantedTrx, cancellationToken);
                return new ProcessResult(1, "Failed Canary.Tests.OrderTotal", string.Empty, 0, TimedOut: false);
            case "format":
                return new ProcessResult(2, $"{ChangedSource}(3,1): warning WHITESPACE: Fix whitespace formatting.", string.Empty, 0, TimedOut: false);
            case "package":
                return new ProcessResult(0, PlantedAdvisory, string.Empty, 0, TimedOut: false);
            default:
                return new ProcessResult(1, $"unexpected arguments: {string.Join(' ', arguments)}", string.Empty, 0, TimedOut: false);
        }
    }
}
