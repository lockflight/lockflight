using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Lifecycle.Approvals;
using Lockflight.Cli.Tests.Ci;

namespace Lockflight.Cli.Tests.Lifecycle;

/// <summary>Structural requirements in the local lifecycle, on a real build: red leaves their tests out, trace runs them on the start's code.</summary>
[Trait("Category", "CiIntegration")]
public sealed class StructuralLifecycleTests : IAsyncLifetime
{
    private const string FeatureId = "001-shipping";
    private const string Doubling = "    public decimal Shipping(decimal kilograms) => kilograms * 2m;";
    private const string CharacterisationFile = "tests/Shop.Tests/ShippingCharacterisationTests.cs";

    private GitHistoryFixture repository = null!;

    public async ValueTask InitializeAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        repository = await GitHistoryFixture.CreateAsync(cancellationToken);
        repository.WriteSolution(Doubling, string.Empty)
            .Write($"specs/{FeatureId}/spec.md", "# Feature Specification: Shipping\n\n## Requirements\n\n- **FR-001** [structural]: Shipping MUST keep costing twice the weight\n")
            .Write($"specs/{FeatureId}/tasks.md", $"- [ ] T001 [US1] (FR-001) Write tests for shipping in {CharacterisationFile}\n");
        Feature feature = new(FeatureId, Path.Combine(repository.Root, "specs", FeatureId), Path.Combine(repository.Root, ".lockflight", "features", FeatureId));
        ApprovalStore approvals = new(feature);
        approvals.Save(ApprovalPhase.Spec, "dev@example.com", DateTimeOffset.UnixEpoch, "0.0.0-test");
        approvals.Save(ApprovalPhase.Tasks, "dev@example.com", DateTimeOffset.UnixEpoch, "0.0.0-test");
        await repository.CommitAsync("init", cancellationToken);
        (ExitCode started, string output, string error) = await RunAsync("redgreen", "start");
        Assert.True(started == ExitCode.Pass, $"{output}\n{error}");
    }

    public ValueTask DisposeAsync()
    {
        repository.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ARedLeavesStructuralTestsOutOfTheCycleAndTraceCoversThemOnTheStartsCode()
    {
        repository.Write(CiSolution.ProductionFile, "namespace Shop;\n\npublic sealed class Price\n{\n    public decimal Shipping(decimal kilograms) => kilograms + kilograms;\n}\n")
            .Write(CharacterisationFile, Characterisation("4m"));

        (ExitCode red, string redOutput, string redError) = await RunAsync("redgreen", "red");
        (ExitCode trace, string traceOutput, string traceError) = await RunAsync("trace");

        Assert.NotEqual(ExitCode.Pass, red);
        Assert.Contains("structural", redOutput + redError, StringComparison.Ordinal);
        Assert.DoesNotContain("GREEN_BEFORE", redOutput, StringComparison.Ordinal);
        Assert.True(trace == ExitCode.Pass, $"{traceOutput}\n{traceError}");
        Assert.Contains("ShippingCharacterisationTests.ShippingIsTwiceTheWeight (structural)", traceOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABehaviourChangeMarkedStructuralIsNotCovered()
    {
        repository.Write(CiSolution.ProductionFile, "namespace Shop;\n\npublic sealed class Price\n{\n    public decimal Shipping(decimal kilograms) => kilograms * 3m;\n}\n")
            .Write(CharacterisationFile, Characterisation("6m"));

        (ExitCode trace, string traceOutput, string traceError) = await RunAsync("trace");

        Assert.True(trace == ExitCode.Fail, $"{traceOutput}\n{traceError}");
        Assert.Contains("behaviour changed — not structural", traceOutput, StringComparison.Ordinal);
    }

    private static string Characterisation(string expected) => $$"""
        using Shop;

        namespace Shop.Tests;

        public sealed class ShippingCharacterisationTests
        {
            [Fact]
            [Trait("Requirement", "FR-001")]
            public void ShippingIsTwiceTheWeight() => Assert.Equal({{expected}}, new Price().Shipping(2m));
        }

        """;

    private async Task<(ExitCode ExitCode, string Output, string Error)> RunAsync(params string[] arguments) =>
        await TestConsole.RunAsync([.. arguments, "--feature", FeatureId], string.Empty, repository.Root,
            new Dictionary<string, string> { ["PATH"] = Environment.GetEnvironmentVariable("PATH") ?? string.Empty, ["HOME"] = Environment.GetEnvironmentVariable("HOME") ?? string.Empty });
}
