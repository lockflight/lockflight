using Lockflight.Cli.Ci;

namespace Lockflight.Cli.Tests.Ci;

[Trait("Category", "CiIntegration")]
public sealed class TestFingerprinterMethodAndBaseTests : TestFingerprinterScenario
{
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
    public async Task AnUnchangedTestKeepsItsFingerprintAcrossACleanRebuildAndANullableContextShift()
    {
        string original = await FingerprintAsync();
        string contextsBefore = NullableContexts();

        foreach (string output in BuildOutputs.Select(path => Path.Combine(repository.Root, path)).Where(Directory.Exists))
        {
            Directory.Delete(output, recursive: true);
        }

        Assert.Equal(original, await FingerprintAsync());

        // Members with nullable reference types outnumber the rest, so the compiler moves the class's [NullableContext] and puts
        // one on the test method: attributes of types it embeds in the assembly, which no person wrote.
        repository.WriteCode(Production, Tests("""
                public static string? First(string? value) => value;

                public static string? Second(string? value) => value;

                public static string? Third(string? value) => value;

                public static object? Fourth(object? value) => value;

            """, expected: "4m"), "CheckBase");
        Assert.Equal(original, await FingerprintAsync());
        Assert.NotEqual(contextsBefore, NullableContexts());
    }

    [Fact]
    public async Task ATestThatIsNotInTheAssemblyHasNoFingerprint()
    {
        await BuildAsync();
        using TestFingerprinter fingerprinter = new(AssemblyPath, new HashSet<string> { "Shop", "Shop.Tests" });

        Assert.Null(fingerprinter.Fingerprint(TestClass, "NoSuchTest"));
        Assert.Null(fingerprinter.Fingerprint("Shop.Tests.NoSuchClass", Test));
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
    public async Task AnOwnTypeArgumentOfAGenericBaseAndAnOwnAttributeOnTheTestCount()
    {
        static string Base(string price, string log) => $$"""
            namespace Shop.Tests;

            public sealed class Seed
            {
                public decimal Price { get; } = {{price}};
            }

            public abstract class SeededBase<T> where T : new()
            {
                protected static T Seeded => new();
            }

            public sealed class LogAttribute : Xunit.v3.BeforeAfterTestAttribute
            {
                public override void Before(System.Reflection.MethodInfo methodUnderTest, Xunit.v3.IXunitTest test) => Console.WriteLine({{log}});
            }

            """;
        const string Tests = """
                [Fact]
                [Log]
                public void Seeds() => Assert.True(Seeded is not null);
            """;
        repository.WriteCode(Production, Tests, "SeededBase<Seed>").Write("tests/Shop.Tests/SeededBase.cs", Base("1m", "1"));
        string original = await FingerprintAsync("Seeds");

        repository.Write("tests/Shop.Tests/SeededBase.cs", Base("2m", "1"));
        string seedChanged = await FingerprintAsync("Seeds");
        Assert.NotEqual(original, seedChanged);

        repository.Write("tests/Shop.Tests/SeededBase.cs", Base("2m", "2"));
        Assert.NotEqual(seedChanged, await FingerprintAsync("Seeds"));
    }
}
