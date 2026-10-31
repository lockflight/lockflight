using System.Text.Json.Nodes;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Ci;

/// <summary><c>lockflight ci</c> end to end on a scripted history: the policy from the target branch, refusals, the evidence it writes.</summary>
public sealed class CiCommandTests : IAsyncLifetime
{
    private readonly DirectoryInfo evidence = Directory.CreateTempSubdirectory("lockflight-ci-out-");
    private GitHistoryFixture repository = null!;
    private string main = string.Empty;

    public async ValueTask InitializeAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        repository = await GitHistoryFixture.CreateAsync(cancellationToken);
        main = await repository.Write("lockflight.yaml", "version: 1\nci:\n  maxRedCommits: 1\n").Write("README.md", "# shop\n").CommitAsync("init", cancellationToken);
        await repository.CheckoutNewAsync("012-checkout", cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        repository.Dispose();
        TempDirectories.DeleteRecursively(evidence);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ThePolicyComesFromTheTargetBranchNotThePullRequest()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await repository.Write("lockflight.yaml", "version: 1\nci:\n  maxRedCommits: 10\n").Write("a.cs", "a").CommitAsync("test: red 1", cancellationToken, null, "Lockflight-Red: 1");
        string head = await repository.Write("b.cs", "b").CommitAsync("test: red 2", cancellationToken, null, "Lockflight-Red: 2");

        await RunAsync(head);

        JsonNode document = Evidence();
        Assert.Equal(main, document["policy"]!["from"]!.GetValue<string>());
        JsonNode red = document["checks"]!.AsArray().Single(check => check!["id"]!.GetValue<string>() == "red")!;
        Assert.Contains("more than ci.maxRedCommits (1)", red["why"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheEvidenceRecordsTheRunAndBothStatusContexts()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        repository.Write(".specify/feature.json", "{\"feature_directory\":\"specs/999-other\"}");
        string head = await repository.Write("a.cs", "a").CommitAsync("test: red 1", cancellationToken, null, "Lockflight-Red: 1");

        (_, string output, _) = await RunAsync(head);

        JsonNode document = Evidence();
        Assert.Equal("lockflight.ci-evidence/v1", document["schema"]!.GetValue<string>());
        Assert.Equal(head, document["head"]!.GetValue<string>());
        Assert.Equal(main, document["base"]!["mergeBase"]!.GetValue<string>());
        Assert.Equal("behaviour", document["changeClass"]!["class"]!.GetValue<string>());
        Assert.Equal(["gates", "lockflight/judge"], document["contexts"]!.AsObject().Select(context => context.Key));
        Assert.Equal([".specify/feature.json"], document["ignored"]!.AsArray().Select(item => item!.GetValue<string>()));
        Assert.Contains("lockflight/judge  NOT-EARNED", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProductionChangeWithoutARedNamesTheManualTrailerFirst()
    {
        string head = await repository.Write("a.cs", "a").CommitAsync("feat", TestContext.Current.CancellationToken);

        await RunAsync(head);

        JsonNode red = Evidence()["checks"]!.AsArray().Single(check => check!["id"]!.GetValue<string>() == "red")!;
        Assert.Equal("NOT-EARNED", red["verdict"]!.GetValue<string>());
        string why = red["why"]!.GetValue<string>();
        Assert.Contains("git commit --trailer \"Lockflight-Red: 1\"", why, StringComparison.Ordinal);
        Assert.True(why.IndexOf("--trailer", StringComparison.Ordinal) < why.IndexOf("redgreen red", StringComparison.Ordinal), why);
    }

    [Fact]
    public async Task AnInvalidManifestOnTheTargetBranchIsAConfigurationError()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await repository.CheckoutAsync("main", cancellationToken);
        main = await repository.Write("lockflight.yaml", "version: 1\nci:\n  maxRedCommits: lots\n").CommitAsync("break policy", cancellationToken);
        await repository.CheckoutAsync("012-checkout", cancellationToken);
        string head = await repository.Write("a.cs", "a").CommitAsync("feat", cancellationToken);

        (ExitCode exitCode, _, string error) = await RunAsync(head);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("the target branch's lockflight.yaml is invalid", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATargetBranchWithoutAManifestIsRefusedWithTheReason()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await repository.CheckoutAsync("main", cancellationToken);
        repository.Delete("lockflight.yaml");
        main = await repository.CommitAsync("drop policy", cancellationToken);
        await repository.CheckoutAsync("012-checkout", cancellationToken);
        await repository.MergeAsync("main", cancellationToken);
        string head = await repository.Write("lockflight.yaml", "version: 1\n").Write("a.cs", "a").CommitAsync("feat", cancellationToken);

        (ExitCode exitCode, _, string error) = await RunAsync(head);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains($"the target branch ({main[..7]}) has no lockflight.yaml", error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(evidence.FullName, "evidence.json")));
    }

    [Fact]
    public async Task AShallowCloneWithoutTheMergeBaseIsRefused()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string head = await repository.Write("a.cs", "a").CommitAsync("feat", cancellationToken);
        using GitHistoryFixture clone = await GitHistoryFixture.CreateAsync(cancellationToken);
        await clone.GitAsync(["fetch", "-q", "--depth", "1", $"file://{repository.Root.Replace('\\', '/')}", "012-checkout", "main"], cancellationToken);

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(
            ["ci", "--platform", "github", "--head", head, "--base", main, "--evidence", evidence.FullName], string.Empty, clone.Root);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("the clone is shallow", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APushIsJudgedOnGreenAndTheGatesOnly()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string head = await repository.Write("a.cs", "a").CommitAsync("feat", cancellationToken);

        await TestConsole.RunAsync(["ci", "--platform", "github", "--event", "push", "--head", head, "--base", main, "--evidence", evidence.FullName], string.Empty, repository.Root);

        JsonArray checks = Evidence()["checks"]!.AsArray();
        Assert.Equal(["gates", "green"], checks.Where(check => check!["requirement"]!.GetValue<string>() == "required").Select(check => check!["id"]!.GetValue<string>()).Order());
        Assert.Equal("[]", Evidence()["redCommits"]!.ToJsonString());
    }

    private Task<(ExitCode ExitCode, string Output, string Error)> RunAsync(string head) =>
        TestConsole.RunAsync(["ci", "--platform", "github", "--head", head, "--base", main, "--branch", "012-checkout", "--evidence", evidence.FullName], string.Empty, repository.Root);

    private JsonNode Evidence() => JsonNode.Parse(File.ReadAllText(Path.Combine(evidence.FullName, "evidence.json")))!;
}
