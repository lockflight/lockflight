using System.Text.Json.Nodes;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Ci;

/// <summary>
/// Tests since red on real builds: a test changed after its red counts only when the head's version, with the head's whole test
/// side, still fails on the red commit's code.
/// </summary>
public abstract class TestsSinceRedScenario : IAsyncLifetime
{
    private protected const string Zero = "    public decimal Shipping(decimal kilograms) => 0m;";
    private protected const string Stub = "    public decimal Shipping(decimal kilograms) => throw new NotImplementedException();";
    private protected const string Implementation = "    public decimal Shipping(decimal kilograms) => kilograms * 2m;";
    private protected const string Test = """
            [Fact]
            public void ShippingIsTwiceTheWeight() => Expect.Same(4m, new Price().Shipping(2m));
        """;

    private readonly DirectoryInfo evidence = Directory.CreateTempSubdirectory("lockflight-ci-since-");
    private protected GitHistoryFixture repository = null!;
    private protected string main = string.Empty;

    public async ValueTask InitializeAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        repository = await GitHistoryFixture.CreateAsync(cancellationToken);
        main = await repository.WriteSolution(string.Empty, string.Empty).Write("tests/Shop.Tests/Expect.cs", Helper("Assert.Equal(expected, actual);"))
            .CommitAsync("init", cancellationToken);
        await repository.CheckoutNewAsync("001-shipping", cancellationToken);
        await repository.WriteCode(Zero, Test).CommitAsync("test: red for US1", cancellationToken, null, "Lockflight-Red: 1");
        await repository.WriteCode(Implementation, Test).CommitAsync("feat: US1", cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        repository.Dispose();
        TempDirectories.DeleteRecursively(evidence);
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    private protected static string Support(string body) => $$"""
        namespace Shop.Support;

        public static class Check
        {
            public static void Same(decimal expected, decimal actual)
            {
                {{body}}
            }
        }

        """;

    private protected static string Helper(string body) => $$"""
        namespace Shop.Tests;

        internal static class Expect
        {
            public static void Same(decimal expected, decimal actual)
            {
                {{body}}
            }
        }

        """;

    private protected async Task<JsonNode> JudgeAsync(string head)
    {
        (ExitCode exitCode, string output, string error) = await TestConsole.RunAsync(
            ["ci", "--platform", "github", "--head", head, "--base", main, "--evidence", evidence.FullName], string.Empty, repository.Root, new Dictionary<string, string> { [EnvironmentVariables.Temp] = evidence.FullName });
        string path = Path.Combine(evidence.FullName, "evidence.json");
        Assert.True(File.Exists(path), $"exit {(int)exitCode}: {output}\n{error}");
        return JsonNode.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken))!;
    }

    private static JsonNode SinceRed(JsonNode document) => document["checks"]!.AsArray().Single(check => check!["id"]!.GetValue<string>() == "tests.sinceRed")!;

    private protected static string Verdict(JsonNode document) => SinceRed(document)["verdict"]!.GetValue<string>();

    private protected static string Why(JsonNode document) => SinceRed(document)["why"]!.GetValue<string>();
}
