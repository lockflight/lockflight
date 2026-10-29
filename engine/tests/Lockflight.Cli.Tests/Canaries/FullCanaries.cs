using System.Text.Json;
using Lockflight.Cli.Canaries;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Canaries;

/// <summary>
/// The canaries that build and run a generated project (<see cref="CanaryProject"/>): the real test gate on a failing test, on every
/// test skipped and on no test, after its harmless twin (a passing test) passed; then a real red whose test fails by
/// <c>Assert.Fail</c>, after a person's approvals and a red/green start; then an amend whose amended test passes on the red-time
/// snapshot, a test file edited after red without an amend at green, and a trace with a project whose evaluation fails. Tens of
/// seconds, mostly the builds.
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
                $"{twin.Describe()}; the .NET SDK and the xUnit packages (NuGet) are needed for these canaries")];
        }

        List<CanaryReport> reports = [];
        foreach ((string name, string members) in Violations)
        {
            project.WriteTests(members);
            GateReading reading = await TestStepAsync(scratch, cancellationToken);
            string canary = $"full: the real test gate does not PASS {name}";
            reports.Add(reading.Test is GateStep test
                ? CanaryReport.NotAPass(canary, test)
                : new CanaryReport(canary, CanaryResult.Broken, $"the gate stopped before the test step: {reading.Describe()}"));
        }

        project.WriteTests(Passing);
        CanaryReport red = await RedAsync(scratch, project, cancellationToken);
        reports.Add(red);
        if (red.Result != CanaryResult.Broken)
        {
            CanaryReport amend = await AmendAsync(scratch, project, cancellationToken);
            reports.Add(amend);
            if (amend.Result != CanaryResult.Broken)
            {
                CanaryReport edited = await EditAfterRedAsync(scratch, project, cancellationToken);
                reports.Add(edited);
                if (edited.Result != CanaryResult.Broken)
                {
                    reports.Add(await UnevaluatedProjectAsync(scratch, project, cancellationToken));
                }
            }
        }

        return reports;
    }

    /// <summary>
    /// With every requirement traced (the green before closed the cycle), a project whose evaluation fails must not let trace PASS: it
    /// may be a test project whose tests would be left out. Its harmless twin, the same trace without that project, must pass.
    /// </summary>
    private static async Task<CanaryReport> UnevaluatedProjectAsync(CanaryScratch scratch, CanaryProject project, CancellationToken cancellationToken)
    {
        const string name = "full: a project whose evaluation fails does not let trace PASS";
        CanaryRun twin = await scratch.RunAsAgentAsync(["trace"], string.Empty, cancellationToken);
        if (twin.ExitCode != ExitCode.Pass)
        {
            return new CanaryReport(name, CanaryResult.Broken, $"the trace with every project evaluating did not pass, so a refusal proves nothing: {twin.FirstLine}");
        }

        project.WriteUnevaluatedProject();
        CanaryRun planted = await scratch.RunAsAgentAsync(["trace"], string.Empty, cancellationToken);
        project.RemoveUnevaluatedProject();
        return planted.ExitCode switch
        {
            ExitCode.NotEarned when planted.Output.Contains("could not be evaluated", StringComparison.Ordinal) => new CanaryReport(name, CanaryResult.Caught, "NOT-EARNED: a project could not be evaluated"),
            ExitCode.Pass => new CanaryReport(name, CanaryResult.Missed, "the trace passed"),
            _ => new CanaryReport(name, CanaryResult.Broken, $"'trace' exited {(int)planted.ExitCode}: {planted.FirstLine}"),
        };
    }

    /// <summary>
    /// In the cycle the amend left red (its implementation written), a test file edited behind the guard, without an amend, must be
    /// refused at green; its harmless twin, the same green once the file is back as its red re-checked it, must pass.
    /// </summary>
    private static async Task<CanaryReport> EditAfterRedAsync(CanaryScratch scratch, CanaryProject project, CancellationToken cancellationToken)
    {
        const string name = "full: a test file edited after red without an amend is refused at green";
        string tests = scratch.PathOf(CanaryProject.TestsFile);
        string checkedContent = await File.ReadAllTextAsync(tests, cancellationToken);
        await File.AppendAllTextAsync(tests, "// planted: edited after red, without an amend\n", cancellationToken);
        CanaryRun planted = await scratch.RunAsAgentAsync(["redgreen", "green"], string.Empty, cancellationToken);
        await File.WriteAllTextAsync(tests, checkedContent, cancellationToken);
        CanaryRun twin = await scratch.RunAsAgentAsync(["redgreen", "green"], string.Empty, cancellationToken);
        if (twin.ExitCode != ExitCode.Pass)
        {
            return new CanaryReport(name, CanaryResult.Broken, $"the green with the test file as its red checked it was refused too, so the refusal proves nothing: {twin.FirstLine}");
        }

        return planted.ExitCode switch
        {
            ExitCode.Fail when planted.Output.Contains("changed after red", StringComparison.Ordinal) => new CanaryReport(name, CanaryResult.Caught, "refused: the file changed after red"),
            ExitCode.Pass => new CanaryReport(name, CanaryResult.Missed, "the green was accepted"),
            _ => new CanaryReport(name, CanaryResult.Broken, $"'redgreen green' exited {(int)planted.ExitCode}: {planted.FirstLine}"),
        };
    }

    /// <summary>
    /// After an honest red and its implementation, a person amends the cycle's test. A re-check of an amended test that passes on the
    /// red-time snapshot (it no longer needs the implementation) must be refused; its harmless twin, an amended test that fails on the
    /// snapshot's stub and passes on the implementation, must then be accepted, or the refusal proves nothing.
    /// </summary>
    private static async Task<CanaryReport> AmendAsync(CanaryScratch scratch, CanaryProject project, CancellationToken cancellationToken)
    {
        const string name = "full: an amended test that passes on the red-time snapshot is refused";
        project.WriteProduction("""

                public bool IsArchived => throw new NotImplementedException();

                public void Archive() => throw new NotImplementedException();

            """);
        project.WriteTests(Passing + """

                [Fact]
                [Trait("Requirement", "FR-001")]
                public void ArchivesTheOrder()
                {
                    Order order = new();
                    order.Archive();
                    Assert.True(order.IsArchived);
                }
            """);
        CanaryRun red = await scratch.RunAsAgentAsync(["redgreen", "red"], string.Empty, cancellationToken);
        if (red.ExitCode != ExitCode.Pass)
        {
            return new CanaryReport(name, CanaryResult.Broken, $"the honest red was not accepted: {red.FirstLine}");
        }

        project.WriteProduction("""

                public bool IsArchived { get; private set; }

                public void Archive() => IsArchived = true;

            """);
        CanaryRun amend = await scratch.RunAsPersonAsync(["redgreen", "amend", "ArchivesTheOrder", "--reason", "canary"], cancellationToken);
        if (amend.ExitCode != ExitCode.Pass)
        {
            return new CanaryReport(name, CanaryResult.Broken, $"'redgreen amend' failed: {amend.FirstLine}");
        }

        project.WriteTests(Passing + """

                [Fact]
                [Trait("Requirement", "FR-001")]
                public void ArchivesTheOrder() => Assert.Equal(0, new Order().LineCount);
            """);
        CanaryRun planted = await scratch.RunAsAgentAsync(["redgreen", "red"], string.Empty, cancellationToken);
        project.WriteTests(Passing + """

                [Fact]
                [Trait("Requirement", "FR-001")]
                public void ArchivesTheOrder()
                {
                    Order order = new();
                    order.Archive();
                    Assert.True(order.IsArchived, "an archived order says so");
                }
            """);
        CanaryRun twin = await scratch.RunAsAgentAsync(["redgreen", "red"], string.Empty, cancellationToken);
        if (twin.ExitCode != ExitCode.Pass)
        {
            return new CanaryReport(name, CanaryResult.Broken, $"an amended test that fails on the red-time stub was refused too, so the refusal proves nothing: {twin.FirstLine}");
        }

        return planted.ExitCode switch
        {
            ExitCode.Fail when planted.Output.Contains("GREEN_BEFORE", StringComparison.Ordinal) => new CanaryReport(name, CanaryResult.Caught, "rejected as GREEN_BEFORE"),
            ExitCode.Pass => new CanaryReport(name, CanaryResult.Missed, "the re-check was accepted"),
            _ => new CanaryReport(name, CanaryResult.Broken, $"'redgreen red' exited {(int)planted.ExitCode}: {planted.FirstLine}"),
        };
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
