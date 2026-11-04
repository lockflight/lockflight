namespace Lockflight.Cli.Tests.Ci;

[Trait("Category", "CiIntegration")]
public sealed class TestFingerprinterDataAndFixtureTests : TestFingerprinterScenario
{
    [Fact]
    public async Task DataMembersFixturesStaticValuesAndOverridesCountToo()
    {
        const string Theory = """
                public static TheoryData<int> Cases => new() { 2 };

                [Theory]
                [MemberData(nameof(Cases))]
                public void UsesFrameworkCode(int factor) => Assert.True(Clock.Now() + Seed.Value + factor > 0);

                private static IClock Clock { get; } = new FakeClock();
            """;
        repository.WriteCode(Production, Theory, "IClassFixture<SeedFixture>")
            .Write("tests/Shop.Tests/Support.cs", Support("1", "2", "3"));
        string original = await FingerprintAsync("UsesFrameworkCode");

        repository.WriteCode(Production, Theory.Replace("new() { 2 }", "new() { 0 }", StringComparison.Ordinal), "IClassFixture<SeedFixture>");
        string data = await FingerprintAsync("UsesFrameworkCode");
        Assert.NotEqual(original, data);

        repository.Write("tests/Shop.Tests/Support.cs", Support("9", "2", "3"));
        string fixture = await FingerprintAsync("UsesFrameworkCode");
        Assert.NotEqual(data, fixture);

        repository.Write("tests/Shop.Tests/Support.cs", Support("9", "8", "3"));
        string value = await FingerprintAsync("UsesFrameworkCode");
        Assert.NotEqual(fixture, value);

        repository.Write("tests/Shop.Tests/Support.cs", Support("9", "8", "7"));
        Assert.NotEqual(value, await FingerprintAsync("UsesFrameworkCode"));
    }

    [Fact]
    public async Task ClassDataGenericInterfaceCallsAndOverridesOfFrameworkTypesCountToo()
    {
        const string Theory = """
                [Theory]
                [ClassData(typeof(Cases))]
                public void UsesSupport(int factor)
                {
                    IConvert convert = new DoubleIt();
                    Stream stream = new OneByte();
                    Assert.True(convert.Run<int>(factor) + stream.ReadByte() > 0);
                }
            """;
        repository.WriteCode(Production, Theory).Write("tests/Shop.Tests/Support.cs", ClassSupport("1", "2", "3"));
        string original = await FingerprintAsync("UsesSupport");

        repository.Write("tests/Shop.Tests/Support.cs", ClassSupport("4", "2", "3"));
        string data = await FingerprintAsync("UsesSupport");
        Assert.NotEqual(original, data);

        repository.Write("tests/Shop.Tests/Support.cs", ClassSupport("4", "5", "3"));
        string generic = await FingerprintAsync("UsesSupport");
        Assert.NotEqual(data, generic);

        repository.Write("tests/Shop.Tests/Support.cs", ClassSupport("4", "5", "6"));
        Assert.NotEqual(generic, await FingerprintAsync("UsesSupport"));
    }

    [Fact]
    public async Task WhatRunsAroundEveryTestCountsToo()
    {
        static string Tests(string cleanup) => $$"""
                [Fact]
                public void Simple() => Assert.True(new Price().Shipping(1m) > 0);

                public void Dispose() => Console.WriteLine({{cleanup}});
            """;
        repository.WriteCode(Production, Tests("1"), "IDisposable");
        string original = await FingerprintAsync("Simple");

        repository.WriteCode(Production, Tests("2"), "IDisposable");

        Assert.NotEqual(original, await FingerprintAsync("Simple"));
    }

    [Fact]
    public async Task ExceptionClausesLocalTypesFixturesAndClassAndAssemblyAttributesCountToo()
    {
        static string Tests(string trait, string collection, string caught, string local) => $$"""
            using Shop;

            [assembly: AssemblyFixture(typeof(Shop.Tests.Warehouse))]

            namespace Shop.Tests;

            [Trait("Area", "{{trait}}")]
            [Collection("{{collection}}")]
            public sealed class PriceTests
            {
                [Fact]
                public void ShippingFollowsTheWeight()
                {
                    {{local}} weight = "2";
                    Assert.NotNull(weight);
                    try { Assert.Equal(4m, new Price().Shipping(2m)); }
                    catch ({{caught}}) { }
                }
            }

            [CollectionDefinition("db")]
            public sealed class DbCollection : ICollectionFixture<DbFixture>;

            """;
        static string Fixtures(string db, string warehouse) => $$"""
            namespace Shop.Tests;

            public sealed class DbFixture
            {
                public DbFixture() => Console.WriteLine({{db}});
            }

            public sealed class Warehouse
            {
                public Warehouse() => Console.WriteLine({{warehouse}});
            }

            """;
        repository.Write(CiSolution.TestsFile, Tests("prices", "db", "ArgumentException", "object")).Write("tests/Shop.Tests/Fixtures.cs", Fixtures("1", "1"));
        List<string> fingerprints = [await FingerprintAsync()];

        repository.Write(CiSolution.TestsFile, Tests("prices", "db", "InvalidOperationException", "object"));
        fingerprints.Add(await FingerprintAsync());
        repository.Write(CiSolution.TestsFile, Tests("prices", "db", "InvalidOperationException", "string"));
        fingerprints.Add(await FingerprintAsync());
        repository.Write(CiSolution.TestsFile, Tests("shipping", "db", "InvalidOperationException", "string"));
        fingerprints.Add(await FingerprintAsync());
        repository.Write("tests/Shop.Tests/Fixtures.cs", Fixtures("2", "1"));
        fingerprints.Add(await FingerprintAsync());
        repository.Write("tests/Shop.Tests/Fixtures.cs", Fixtures("2", "2"));
        fingerprints.Add(await FingerprintAsync());

        string[] steps = ["caught type", "local type", "class attribute", "collection fixture", "assembly fixture"];
        for (int step = 0; step < steps.Length; step++)
        {
            Assert.True(fingerprints[step] != fingerprints[step + 1], $"the {steps[step]} did not change the fingerprint");
        }
    }
}
