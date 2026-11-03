using System.Text.Json.Nodes;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Ci;

/// <summary>
/// The judge re-runs each red commit on a real build: its new tests must fail in production code there, as <c>redgreen red</c>
/// demands locally, whatever the trailer claims.
/// </summary>
public abstract class RedAtRedCommitsScenario : IAsyncLifetime
{
    private protected const string Stub = "    public decimal Shipping(decimal kilograms) => throw new NotImplementedException();";
    private protected const string Implementation = "    public decimal Shipping(decimal kilograms) => kilograms * 2m;";
    private protected const string ShippingTest = """
            [Fact]
            [Trait("Requirement", "FR-001")]
            public void ShippingIsTwicTheWeight() => Assert.Equal(4m, new Price().Shipping(2m));
        """;

    private protected readonly DirectoryInfo evidence = Directory.CreateTempSubdirectory("lockflight-ci-red-");
    private protected GitHistoryFixture repository = null!;
    private protected string main = string.Empty;

    public async ValueTask InitializeAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        repository = await GitHistoryFixture.CreateAsync(cancellationToken);
        main = await repository.WriteSolution(production: string.Empty, tests: string.Empty).CommitAsync("init", cancellationToken);
        await repository.CheckoutNewAsync("001-shipping", cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        repository.Dispose();
        TempDirectories.DeleteRecursively(evidence);
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    private protected async Task<JsonNode> JudgeAsync(string head)
    {
        (ExitCode exitCode, string output, string error) = await TestConsole.RunAsync(
            ["ci", "--platform", "github", "--head", head, "--base", main, "--evidence", evidence.FullName], string.Empty, repository.Root, new Dictionary<string, string> { [EnvironmentVariables.Temp] = evidence.FullName });
        string path = Path.Combine(evidence.FullName, "evidence.json");
        Assert.True(File.Exists(path), $"exit {(int)exitCode}: {output}\n{error}");
        return JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(evidence.FullName, "evidence.json"), TestContext.Current.CancellationToken))!;
    }

    private protected static JsonNode Check(JsonNode document, string id) => document["checks"]!.AsArray().Single(check => check!["id"]!.GetValue<string>() == id)!;
}
