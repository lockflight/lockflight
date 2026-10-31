using Lockflight.Cli.Ci;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Ci;

/// <summary>
/// The per-method fingerprint on real builds: unchanged when a later cycle adds tests above the test in the same class or the
/// repository's assembly version changes; changed when the test, a helper it calls in another file, an inherited method or its
/// data changes.
/// </summary>
[Trait("Category", "CiIntegration")]
public sealed class TestFingerprinterTests : IAsyncLifetime
{
    private const string TestClass = "Shop.Tests.PriceTests";
    private const string Test = "ShippingFollowsTheWeight";
    private const string Production = "    public decimal Shipping(decimal kilograms) => kilograms * 2m;";

    private GitHistoryFixture repository = null!;

    public async ValueTask InitializeAsync()
    {
        repository = await GitHistoryFixture.CreateAsync(TestContext.Current.CancellationToken);
        repository.WriteSolution(Production, string.Empty)
            .WriteCode(Production, Tests(string.Empty, expected: "4m"), "CheckBase")
            .Write("tests/Shop.Tests/Weights.cs", Weights("2m"))
            .Write("tests/Shop.Tests/CheckBase.cs", CheckBase("value > 0"));
    }

    public ValueTask DisposeAsync()
    {
        repository.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task AFingerprintChangesOnlyWithWhatTheTestRuns()
    {
        string original = await FingerprintAsync();

        repository.WriteCode(Production, Tests("""
                [Fact]
                public async Task AddedAbove() { await Task.Yield(); Assert.True(Run(() => 1) == 1); }

                [Theory]
                [InlineData(5)]
                public void AlsoAbove(int value) => Assert.Equal(value, Run(() => value));

            """, expected: "4m"), "CheckBase");
        Assert.Equal(original, await FingerprintAsync());

        repository.Write("src/Shop/Shop.csproj", File.ReadAllText(Path.Combine(repository.Root, "src/Shop/Shop.csproj")).Replace("<Nullable>", "<AssemblyVersion>9.8.7.6</AssemblyVersion>\n    <Nullable>", StringComparison.Ordinal));
        Assert.Equal(original, await FingerprintAsync());

        repository.Write("tests/Shop.Tests/Weights.cs", Weights("3m"));
        string helperChanged = await FingerprintAsync();
        Assert.NotEqual(original, helperChanged);

        repository.Write("tests/Shop.Tests/CheckBase.cs", CheckBase("value >= 0"));
        string baseChanged = await FingerprintAsync();
        Assert.NotEqual(helperChanged, baseChanged);

        repository.WriteCode(Production, Tests(string.Empty, expected: "6m"), "CheckBase");
        Assert.NotEqual(baseChanged, await FingerprintAsync());
    }

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
    public async Task ATestInheritedFromAGenericBaseHasAFingerprintThatFollowsTheBaseAndItsTypeArgument()
    {
        static string Base(string length) => $$"""
            namespace Shop.Tests;

            public abstract class PriceTestsBase<T>
            {
                [Fact]
                public void Inherited() => Assert.True(typeof(T).Name.Length > {{length}});
            }

            """;
        repository.WriteCode(Production, string.Empty, "PriceTestsBase<decimal>").Write("tests/Shop.Tests/PriceTestsBase.cs", Base("1"));
        string original = await FingerprintAsync("Inherited");

        repository.Write("tests/Shop.Tests/PriceTestsBase.cs", Base("2"));
        string body = await FingerprintAsync("Inherited");
        Assert.NotEqual(original, body);

        repository.WriteCode(Production, string.Empty, "PriceTestsBase<int>");
        Assert.NotEqual(body, await FingerprintAsync("Inherited"));
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

    [Fact]
    public async Task ATestThatIsNotInTheAssemblyHasNoFingerprint()
    {
        await BuildAsync();
        using TestFingerprinter fingerprinter = new(AssemblyPath, new HashSet<string> { "Shop", "Shop.Tests" });

        Assert.Null(fingerprinter.Fingerprint(TestClass, "NoSuchTest"));
        Assert.Null(fingerprinter.Fingerprint("Shop.Tests.NoSuchClass", Test));
    }

    private string AssemblyPath => Path.Combine(repository.Root, "tests", "Shop.Tests", "bin", "Debug", "net10.0", "Shop.Tests.dll");

    private static string Support(string fixtureSeed, string staticValue, string clock) => $$"""
        namespace Shop.Tests;

        public sealed class SeedFixture
        {
            public SeedFixture() => Seeded = {{fixtureSeed}};

            public int Seeded { get; }
        }

        internal static class Seed
        {
            public static readonly int Value = {{staticValue}};
        }

        public interface IClock
        {
            int Now();
        }

        internal sealed class FakeClock : IClock
        {
            public int Now() => {{clock}};
        }

        """;

    private static string ClassSupport(string data, string factor, string value) => $$"""
        namespace Shop.Tests;

        public sealed class Cases : TheoryData<int>
        {
            public Cases() => Add({{data}});
        }

        public interface IConvert
        {
            int Run<T>(int value);
        }

        internal sealed class DoubleIt : IConvert
        {
            public int Run<T>(int value) => value * {{factor}};
        }

        internal sealed class OneByte : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => 1;
            public override long Position { get; set; }
            public override int ReadByte() => {{value}};
            public override int Read(byte[] buffer, int offset, int count) => 0;
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => 0;
            public override void SetLength(long value) { }
            public override void Write(byte[] buffer, int offset, int count) { }
        }

        """;

    private async Task<string> FingerprintAsync(string test = Test)
    {
        await BuildAsync();
        using TestFingerprinter fingerprinter = new(AssemblyPath, new HashSet<string> { "Shop", "Shop.Tests" });
        string? fingerprint = fingerprinter.Fingerprint(TestClass, test);
        Assert.NotNull(fingerprint);
        return fingerprint;
    }

    private async Task BuildAsync()
    {
        ProcessResult build = await ProcessRunner.RunAsync("dotnet", ["build", "--nologo", "-v", "q"], repository.Root, TimeSpan.FromMinutes(5), TestContext.Current.CancellationToken);
        Assert.True(build.ExitCode == 0, build.Combined);
    }

    private static string Tests(string above, string expected) => $$"""
            {{above}}
            [Theory]
            [InlineData(2)]
            [Trait("Requirement", "FR-001")]
            public async Task {{Test}}(int factor)
            {
                await Task.Yield();
                Assert.Equal({{expected}}, Run(() => new Price().Shipping(Weights.Two * factor / 2)));
                Assert.True(Check(factor));
            }

            private static T Run<T>(Func<T> action) => action();
        """;

    private static string Weights(string two) => $$"""
        namespace Shop.Tests;

        internal static class Weights
        {
            public static decimal Two => {{two}};
        }

        """;

    private static string CheckBase(string condition) => $$"""
        namespace Shop.Tests;

        public abstract class CheckBase
        {
            protected static bool Check(int value) => {{condition}};
        }

        """;
}
