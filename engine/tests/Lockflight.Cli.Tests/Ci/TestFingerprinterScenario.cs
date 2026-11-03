using Lockflight.Cli.Ci;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Ci;

/// <summary>
/// The per-method fingerprint on real builds: unchanged when a later cycle adds tests above the test in the same class or the
/// repository's assembly version changes; changed when the test, a helper it calls in another file, an inherited method or its
/// data changes.
/// </summary>
public abstract class TestFingerprinterScenario : IAsyncLifetime
{
    private protected const string TestClass = "Shop.Tests.PriceTests";
    private protected const string Test = "ShippingFollowsTheWeight";
    private protected const string Production = "    public decimal Shipping(decimal kilograms) => kilograms * 2m;";

    private protected static readonly string[] BuildOutputs = ["src/Shop/bin", "src/Shop/obj", "tests/Shop.Tests/bin", "tests/Shop.Tests/obj"];

    private protected GitHistoryFixture repository = null!;

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
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    /// <summary>Every <c>[NullableContext]</c> on the test class and its methods with its value, to show the compiler moved them.</summary>
    private protected string NullableContexts()
    {
        using System.Reflection.PortableExecutable.PEReader pe = new(File.OpenRead(AssemblyPath));
        System.Reflection.Metadata.MetadataReader reader = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
        System.Reflection.Metadata.TypeDefinition type = reader.TypeDefinitions.Select(reader.GetTypeDefinition)
            .Single(candidate => $"{reader.GetString(candidate.Namespace)}.{reader.GetString(candidate.Name)}" == TestClass);
        IEnumerable<(string Member, System.Reflection.Metadata.CustomAttributeHandleCollection Attributes)> members =
            [("class", type.GetCustomAttributes()), .. type.GetMethods().Select(reader.GetMethodDefinition).Select(method => (reader.GetString(method.Name), method.GetCustomAttributes()))];
        return string.Join(';', members.SelectMany(member => member.Attributes.Select(reader.GetCustomAttribute)
            .Where(attribute => AttributeTypeName(reader, attribute) == "NullableContextAttribute")
            .Select(attribute => $"{member.Member}={Convert.ToHexString(reader.GetBlobBytes(attribute.Value))}")));
    }

    private static string AttributeTypeName(System.Reflection.Metadata.MetadataReader reader, System.Reflection.Metadata.CustomAttribute attribute) => attribute.Constructor.Kind switch
    {
        System.Reflection.Metadata.HandleKind.MethodDefinition => reader.GetString(reader.GetTypeDefinition(reader.GetMethodDefinition((System.Reflection.Metadata.MethodDefinitionHandle)attribute.Constructor).GetDeclaringType()).Name),
        System.Reflection.Metadata.HandleKind.MemberReference when reader.GetMemberReference((System.Reflection.Metadata.MemberReferenceHandle)attribute.Constructor).Parent is { Kind: System.Reflection.Metadata.HandleKind.TypeReference } parent
            => reader.GetString(reader.GetTypeReference((System.Reflection.Metadata.TypeReferenceHandle)parent).Name),
        _ => string.Empty,
    };

    private protected string AssemblyPath => Path.Combine(repository.Root, "tests", "Shop.Tests", "bin", "Debug", "net10.0", "Shop.Tests.dll");

    private protected static string Support(string fixtureSeed, string staticValue, string clock) => $$"""
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

    private protected static string ClassSupport(string data, string factor, string value) => $$"""
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

    private protected async Task<string> FingerprintAsync(string test = Test)
    {
        await BuildAsync();
        using TestFingerprinter fingerprinter = new(AssemblyPath, new HashSet<string> { "Shop", "Shop.Tests" });
        string? fingerprint = fingerprinter.Fingerprint(TestClass, test);
        Assert.NotNull(fingerprint);
        return fingerprint;
    }

    private protected async Task BuildAsync()
    {
        ProcessResult build = await ProcessRunner.RunAsync("dotnet", ["build", "--nologo", "-v", "q"], repository.Root, TimeSpan.FromMinutes(5), TestContext.Current.CancellationToken);
        Assert.True(build.ExitCode == 0, build.Combined);
    }

    private protected static string Tests(string above, string expected) => $$"""
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

    private protected static string Weights(string two) => $$"""
        namespace Shop.Tests;

        internal static class Weights
        {
            public static decimal Two => {{two}};
        }

        """;

    private protected static string CheckBase(string condition) => $$"""
        namespace Shop.Tests;

        public abstract class CheckBase
        {
            protected static bool Check(int value) => {{condition}};
        }

        """;
}
