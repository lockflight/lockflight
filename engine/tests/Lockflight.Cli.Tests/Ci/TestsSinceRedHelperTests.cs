using System.Text.Json.Nodes;

namespace Lockflight.Cli.Tests.Ci;

[Trait("Category", "CiIntegration")]
public sealed class TestsSinceRedHelperTests : TestsSinceRedScenario
{
    [Fact]
    public async Task AHelperGuttedInAnotherFileIsPartOfTheRecheck()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string head = await repository.Write("tests/Shop.Tests/Expect.cs", Helper("_ = expected; _ = actual;")).CommitAsync("refactor: helper", cancellationToken);

        JsonNode document = await JudgeAsync(head);

        Assert.Equal("FAIL", Verdict(document));
        Assert.Contains("GREEN_BEFORE", Why(document), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAssemblyFixtureAddedAfterRedIsPartOfTheRecheck()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        const string Seam = "    public static decimal? Override { get; set; }\n";
        await repository.WriteCode(Seam + "    public decimal Shipping(decimal kilograms) => Override ?? throw new NotImplementedException();", Test)
            .CommitAsync("test: red for US1 again", cancellationToken, null, "Lockflight-Red: 1");
        string head = await repository.WriteCode(Seam + "    public decimal Shipping(decimal kilograms) => Override ?? 0m;", Test)
            .Write("tests/Shop.Tests/Patch.cs", "[assembly: AssemblyFixture(typeof(Shop.Tests.Patch))]\n\nnamespace Shop.Tests;\n\npublic sealed class Patch\n{\n    public Patch() => Shop.Price.Override = 4m;\n}\n")
            .CommitAsync("feat: US1", cancellationToken);

        JsonNode document = await JudgeAsync(head);

        Assert.Equal("FAIL", Verdict(document));
        Assert.Contains("GREEN_BEFORE", Why(document), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFrameworkFreeSupportProjectChangedAfterRedIsCompared()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string supportTest = Test.Replace("Expect.Same", "Shop.Support.Check.Same", StringComparison.Ordinal);
        await repository.Write("tests/Shop.Support/Shop.Support.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n")
            .Write("tests/Shop.Support/Check.cs", Support("if (expected != actual) throw new System.InvalidOperationException($\"{expected} != {actual}\");"))
            .Write("tests/Shop.Tests/Shop.Tests.csproj", (await File.ReadAllTextAsync(Path.Combine(repository.Root, "tests/Shop.Tests/Shop.Tests.csproj"), cancellationToken))
                .Replace("<ProjectReference Include=\"../../src/Shop/Shop.csproj\" />", "<ProjectReference Include=\"../../src/Shop/Shop.csproj\" />\n    <ProjectReference Include=\"../Shop.Support/Shop.Support.csproj\" />", StringComparison.Ordinal))
            .WriteCode(Stub, supportTest).CommitAsync("test: red for US1 again", cancellationToken, null, "Lockflight-Red: 1");
        string head = await repository.Write("tests/Shop.Support/Check.cs", Support("_ = expected; _ = actual;"))
            .WriteCode("    public decimal Shipping(decimal kilograms) => 0m;", supportTest).CommitAsync("feat: US1", cancellationToken);

        JsonNode document = await JudgeAsync(head);

        Assert.Equal("NOT-EARNED", Verdict(document));
        Assert.Equal("amend-changed-expectation", document["cycles"]![0]!["tests"]![0]!["sinceRed"]!.GetValue<string>());
    }
}
