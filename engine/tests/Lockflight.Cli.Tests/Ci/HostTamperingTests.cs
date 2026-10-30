using System.Text.Json.Nodes;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Ci;

/// <summary>
/// Code in the test assemblies that could forge a result, on real builds: new in the pull request is NOT-EARNED and listed; what
/// the base already had, or its baseline accepts, is information.
/// </summary>
[Trait("Category", "CiIntegration")]
public sealed class HostTamperingTests : IAsyncLifetime
{
    private const string Stub = "    public decimal Shipping(decimal kilograms) => throw new NotImplementedException();";
    private const string Test = "    [Fact]\n    public void ShippingIsTwiceTheWeight() => Assert.Equal(4m, new Price().Shipping(2m));";
    private const string OldFixture = """
        namespace Shop.Tests;

        internal static class Tools
        {
            public static void Run() => System.Diagnostics.Process.Start("dotnet", "--info");
        }

        """;

    private readonly DirectoryInfo evidence = Directory.CreateTempSubdirectory("lockflight-ci-tamper-");
    private GitHistoryFixture repository = null!;
    private string main = string.Empty;

    public async ValueTask InitializeAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        repository = await GitHistoryFixture.CreateAsync(cancellationToken);
        main = await repository.WriteSolution(string.Empty, string.Empty).Write("tests/Shop.Tests/Tools.cs", OldFixture).CommitAsync("init", cancellationToken);
        await repository.CheckoutNewAsync("001-shipping", cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        repository.Dispose();
        TempDirectories.DeleteRecursively(evidence);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task CodeTheBaseAlreadyHadIsInformationOnly()
    {
        string head = await repository.WriteCode(Stub, Test).CommitAsync("test: red", TestContext.Current.CancellationToken, null, "Lockflight-Red: 1");

        JsonNode document = await JudgeAsync(head);

        Assert.Equal("PASS", Check(document)["verdict"]!.GetValue<string>());
        Assert.Equal("process-start", document["hostTampering"]!["existing"]![0]!["kind"]!.GetValue<string>());
    }

    [Fact]
    public async Task ANewModuleInitializerAndFileWriteAreNotEarnedAndListed()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await repository.WriteCode(Stub, Test).CommitAsync("test: red", cancellationToken, null, "Lockflight-Red: 1");
        string head = await repository.WriteCode("    public decimal Shipping(decimal kilograms) => kilograms * 2m;", Test).Write("tests/Shop.Tests/Setup.cs", """
            using System.Runtime.CompilerServices;

            namespace Shop.Tests;

            internal static class Setup
            {
                [ModuleInitializer]
                internal static void Start() => AppDomain.CurrentDomain.ProcessExit += async (_, _) => await File.WriteAllTextAsync("results.xml", "<pass/>");
            }

            """).CommitAsync("feat: US1", cancellationToken);

        JsonNode document = await JudgeAsync(head);

        Assert.Equal("NOT-EARNED", Check(document)["verdict"]!.GetValue<string>());
        string[] kinds = [.. document["hostTampering"]!["new"]!.AsArray().Select(finding => finding!["kind"]!.GetValue<string>()).Distinct().Order()];
        Assert.Equal(["file-write", "module-initializer", "process-exit"], kinds);
        Assert.Contains("Shop.Tests.Setup::Start", Check(document)["why"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFindingTheBasesBaselineAcceptsDoesNotBlock()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await repository.CheckoutAsync("main", cancellationToken);
        main = await repository.Write(".lockflight/baselines/host-tampering.json", """{ "schema": "lockflight.baseline.host-tampering/v1", "findings": ["Shop.Tests|exit|Shop.Tests.Guard::Stop"] }""")
            .CommitAsync("accept the guard", cancellationToken);
        await repository.CheckoutAsync("001-shipping", cancellationToken);
        await repository.MergeAsync("main", cancellationToken);
        string head = await repository.WriteCode(Stub, Test).Write("tests/Shop.Tests/Guard.cs", "namespace Shop.Tests;\n\ninternal static class Guard\n{\n    public static void Stop() => Environment.Exit(3);\n}\n")
            .CommitAsync("test: red", cancellationToken, null, "Lockflight-Red: 1");

        Assert.Equal("PASS", Check(await JudgeAsync(head))["verdict"]!.GetValue<string>());
    }

    private async Task<JsonNode> JudgeAsync(string head)
    {
        (ExitCode exitCode, string output, string error) = await TestConsole.RunAsync(
            ["ci", "--platform", "github", "--head", head, "--base", main, "--evidence", evidence.FullName], string.Empty, repository.Root, new Dictionary<string, string> { [EnvironmentVariables.Temp] = evidence.FullName });
        string path = Path.Combine(evidence.FullName, "evidence.json");
        Assert.True(File.Exists(path), $"exit {(int)exitCode}: {output}\n{error}");
        return JsonNode.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken))!;
    }

    private static JsonNode Check(JsonNode document) => document["checks"]!.AsArray().Single(check => check!["id"]!.GetValue<string>() == "tests.hostTampering")!;
}
