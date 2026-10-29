using System.Text.Json.Nodes;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Tests.Review;

namespace Lockflight.Cli.Tests.Ci;

/// <summary><c>lockflight ci review</c>: the review job's step, with a stand-in for Claude Code.</summary>
public sealed class CiReviewTests : IAsyncLifetime
{
    private const string Feature = "001-discount";

    private readonly DirectoryInfo output = Directory.CreateTempSubdirectory("lockflight-ci-review-");
    private readonly DirectoryInfo tools = Directory.CreateTempSubdirectory("lockflight-ci-review-path-");
    private readonly FakeClaude claude = new();
    private GitHistoryFixture repository = null!;
    private string main = string.Empty;
    private string head = string.Empty;

    public async ValueTask InitializeAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        repository = await GitHistoryFixture.CreateAsync(cancellationToken);
        main = await repository.Write("lockflight.yaml", "version: 1\nreview:\n  model: opus\n  maxUsd: 1.50\n")
            .Write($"specs/{Feature}/spec.md", "- **FR-001**: The total applies the discount.\n").Write("src/Orders.cs", "namespace Shop;\n").CommitAsync("start", cancellationToken);
        await repository.CheckoutNewAsync(Feature, cancellationToken);
        head = await repository.Write("lockflight.yaml", "version: 1\nreview:\n  model: haiku\n  maxUsd: 0.10\n")
            .Write("src/Orders.cs", "namespace Shop;\n\npublic static class Orders { public static decimal Total(decimal price) => price; }\n")
            .Write($".lockflight/features/{Feature}/reviews/20260101-000000.json", """{"schema":"lockflight.review/v2","at":"2026-01-01T00:00:00Z","model":"opus","base":{"ref":"main","target":"main"},"notOfRecord":[],"findings":[{"id":"abc123abc123","severity":"major","outcome":"confirmed","state":"open","file":"src/Orders.cs","line":3,"title":"Planted","evidence":"x"}]}""")
            .CommitAsync("total", cancellationToken);
        File.WriteAllText(Path.Combine(tools.FullName, OperatingSystem.IsWindows() ? "claude.exe" : "claude"), string.Empty);
    }

    public ValueTask DisposeAsync()
    {
        repository.Dispose();
        TempDirectories.DeleteRecursively(output);
        TempDirectories.DeleteRecursively(tools);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task TheReviewUsesTheTargetBranchsPolicyAndReadsNoEarlierRunFromTheTree()
    {
        claude.Answers();

        (ExitCode exitCode, string printed, string error) = await RunAsync();

        Assert.True(exitCode == ExitCode.Pass, $"{printed}\n{error}");
        IReadOnlyList<string> call = claude.Calls.Single(arguments => arguments.Contains("-p"));
        Assert.Equal("opus", call[call.ToList().IndexOf("--model") + 1]);
        Assert.Equal("1.50", call[call.ToList().IndexOf("--max-budget-usd") + 1]);
        JsonNode result = JsonNode.Parse(File.ReadAllText(Path.Combine(output.FullName, "review-result.json")))!;
        Assert.Equal("lockflight.ci-review/v1", result["schema"]!.GetValue<string>());
        Assert.Equal(head, result["head"]!.GetValue<string>());
        Assert.Equal("PASS", result["verdict"]!.GetValue<string>());
        Assert.Single(Directory.GetFiles(Path.Combine(output.FullName, Feature, "reviews"), "*.json"));
    }

    [Fact]
    public async Task AnUnverifiedFindingIsNotEarned()
    {
        claude.Answers(FakeClaude.Unverified("Discount ignored"));

        (ExitCode exitCode, _, _) = await RunAsync();

        Assert.Equal(ExitCode.NotEarned, exitCode);
        Assert.Equal("NOT-EARNED", JsonNode.Parse(File.ReadAllText(Path.Combine(output.FullName, "review-result.json")))!["verdict"]!.GetValue<string>());
    }

    [Fact]
    public async Task ACheckoutAtAnotherCommitIsRefused()
    {
        await repository.CheckoutAsync("main", TestContext.Current.CancellationToken);

        Assert.Equal(ExitCode.Config, (await RunAsync()).ExitCode);
    }

    private async Task<(ExitCode ExitCode, string Output, string Error)> RunAsync()
    {
        using StringWriter printed = new();
        using StringWriter error = new();
        CliContext context = new(new StringReader(string.Empty), printed, error, repository.Root, TimeProvider.System)
        {
            Environment = EnvironmentReader.Of(new Dictionary<string, string> { [EnvironmentVariables.Path] = tools.FullName }),
            Claude = claude.RunAsync,
        };
        ExitCode exitCode = await CliApplication.RunAsync(
            ["ci", "review", "--platform", "github", "--head", head, "--base", main, "--branch", Feature, "--out", output.FullName], context, TestContext.Current.CancellationToken);
        return (exitCode, printed.ToString(), error.ToString());
    }
}
