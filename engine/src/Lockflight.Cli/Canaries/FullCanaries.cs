using System.Text.Json;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Canaries;

/// <summary>
/// The canaries that build and run a generated project (<see cref="CanaryProject"/>): the real test gate on a failing test, on every
/// test skipped and on no test, after its harmless twin (a passing test) passed; then a real red whose test fails by
/// <c>Assert.Fail</c>, after a person's approvals and a red/green start. Tens of seconds, mostly the build.
/// </summary>
internal static class FullCanaries
{
    private const string ResultFileName = "gate-result.json";

    private const string TestStepName = "test";

    private const string Passing = """
            [Fact]
            public void AnEmptyOrderHasNoLines() => Assert.Equal(0, new Order().LineCount);
        """;

    private static readonly (string Case, string Members)[] Violations =
    [
        ("a failing test", Passing + """

                [Fact]
                public void CountsALine() => Assert.Equal(1, new Order().LineCount);
            """),
        ("every test skipped", """
                [Fact(Skip = "later")]
                public void AnEmptyOrderHasNoLines() => Assert.Equal(0, new Order().LineCount);
            """),
        ("zero tests", """
                public static int Helper() => new Order().LineCount;
            """),
    ];

    public static async Task<IReadOnlyList<CanaryReport>> RunAsync(TimeProvider time, CancellationToken cancellationToken)
    {
        using CanaryScratch scratch = new(time);
        CanaryProject project = new(scratch);
        if (await project.CreateAsync(cancellationToken) is string notCreated)
        {
            return [new CanaryReport("full: the canary project is generated", CanaryResult.Broken, notCreated)];
        }

        GateReading twin = await TestStepAsync(scratch, cancellationToken);
        if (twin.Test is not { Verdict: Verdict.Pass })
        {
            return [new CanaryReport("full: a passing test passes the real test gate", CanaryResult.Broken,
                $"{twin.Describe()}; the .NET SDK and the xUnit packages (NuGet) are needed for --canaries=full")];
        }

        List<CanaryReport> reports = [];
        foreach ((string name, string members) in Violations)
        {
            project.WriteTests(members);
            GateReading reading = await TestStepAsync(scratch, cancellationToken);
            string canary = $"full: the real test gate does not PASS {name}";
            reports.Add(reading.Test is GateStep test
                ? TestGateCanaries.Report(canary, test)
                : new CanaryReport(canary, CanaryResult.Broken, $"the gate stopped before the test step: {reading.Describe()}"));
        }

        project.WriteTests(Passing);
        reports.Add(await RedAsync(scratch, project, cancellationToken));
        return reports;
    }

    private static async Task<CanaryReport> RedAsync(CanaryScratch scratch, CanaryProject project, CancellationToken cancellationToken)
    {
        const string name = "full: a real red failing by Assert.Fail is rejected";
        if (await project.AddFeatureAsync(cancellationToken) is string notAdded)
        {
            return new CanaryReport(name, CanaryResult.Broken, notAdded);
        }

        foreach (string[] step in new[] { new[] { "approve", "spec" }, ["approve", "tasks"] })
        {
            CanaryRun approval = await scratch.RunAsPersonAsync(step, cancellationToken);
            if (approval.ExitCode != ExitCode.Pass)
            {
                return new CanaryReport(name, CanaryResult.Broken, $"'{string.Join(' ', step)}' failed: {approval.FirstLine}");
            }
        }

        CanaryRun start = await scratch.RunAsAgentAsync(["redgreen", "start"], string.Empty, cancellationToken);
        if (start.ExitCode != ExitCode.Pass)
        {
            return new CanaryReport(name, CanaryResult.Broken, $"'redgreen start' failed: {start.FirstLine}");
        }

        project.WriteProduction("""

                public void Archive() => throw new NotImplementedException();

            """);
        project.WriteTests(Passing + """

                [Fact]
                [Trait("Requirement", "FR-001")]
                public void ArchivesTheOrder() => Assert.Fail("planted: fails whatever Order does");
            """);
        CanaryRun red = await scratch.RunAsAgentAsync(["redgreen", "red"], string.Empty, cancellationToken);
        return red.ExitCode switch
        {
            ExitCode.Fail when red.Output.Contains("RED_IN_TEST", StringComparison.Ordinal) => new CanaryReport(name, CanaryResult.Caught, "rejected as RED_IN_TEST"),
            ExitCode.Pass => new CanaryReport(name, CanaryResult.Missed, "the red was accepted"),
            _ => new CanaryReport(name, CanaryResult.Broken, $"'redgreen red' exited {(int)red.ExitCode}: {red.FirstLine}"),
        };
    }

    /// <summary>Runs the stop profile on the project and reads its steps from the result file.</summary>
    private static async Task<GateReading> TestStepAsync(CanaryScratch scratch, CancellationToken cancellationToken)
    {
        string result = Path.Combine(scratch.Home, ResultFileName);
        File.Delete(result);
        CanaryRun run = await scratch.RunAsAgentAsync(["gate", "run", "--profile", "stop", "--result", result], string.Empty, cancellationToken);
        if (!File.Exists(result))
        {
            return new GateReading(null, null, $"gate run exited {(int)run.ExitCode} without a result: {run.FirstLine}");
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(result));
        GateStep[] steps = [.. document.RootElement.GetProperty("steps").EnumerateArray().Select(ReadStep)];
        return new GateReading(steps.FirstOrDefault(step => step.Name == TestStepName), steps.LastOrDefault(), null);
    }

    private static GateStep ReadStep(JsonElement step)
    {
        string verdict = step.GetProperty("verdict").GetString() ?? string.Empty;
        return new GateStep(
            step.GetProperty("name").GetString() ?? string.Empty,
            step.GetProperty("toolExitCode").GetInt32(),
            Enum.GetValues<Verdict>().FirstOrDefault(value => VerdictNames.ToContractName(value) == verdict, Verdict.Error),
            step.GetProperty("durationMs").GetInt64(),
            step.GetProperty("detail").GetString() ?? string.Empty);
    }
}
